using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed record WinFormsMenuItem(string Name, string Parent, string Text, bool IsSeparator = false);

/// <summary>Reads and edits built-in menu trees using the shared VB syntax model.</summary>
public sealed partial class WinFormsMenuDocument
{
    private readonly SourceText _source;
    private readonly ClassBlockSyntax _class;
    private readonly MethodBlockSyntax _initialize;
    private readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsupportedTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Parent, string Child, StatementSyntax Statement)> _links = [];

    // Shapes this editor cannot safely rewrite are recorded here instead of being thrown
    // from the constructor: one MenuStrip or ContextMenuStrip with an unusual Items/
    // DropDownItems initialization must not stop every other menu, or the control, or the
    // whole form, from opening. Read() still throws for the affected parent so existing
    // callers that catch NotSupportedException keep working unchanged.
    private readonly Dictionary<string, string> _unsupportedParents = new(StringComparer.OrdinalIgnoreCase);

    public WinFormsMenuDocument(string source)
        : this(SourceText.From(source), VisualBasicSyntaxTree.ParseText(source).GetRoot())
    {
    }

    internal WinFormsMenuDocument(SourceText source, SyntaxNode root)
    {
        _source = source;
        _initialize = root.DescendantNodes().OfType<MethodBlockSyntax>().Single(method =>
            method.SubOrFunctionStatement.Identifier.ValueText.Equals("InitializeComponent", StringComparison.OrdinalIgnoreCase));
        _class = _initialize.FirstAncestorOrSelf<ClassBlockSyntax>()!;
        foreach (var declarator in _class.Members.OfType<FieldDeclarationSyntax>().SelectMany(field => field.Declarators))
        {
            var type = declarator.AsClause is SimpleAsClauseSyntax simple ? simple.Type.ToString() : "";
            foreach (var name in declarator.Names) _types[name.Identifier.ValueText] = type;
        }
        foreach (var statement in _initialize.Statements)
        {
            if (statement is AssignmentStatementSyntax { Left: MemberAccessExpressionSyntax left } assignment &&
                left.Name.Identifier.ValueText.Equals("Text", StringComparison.OrdinalIgnoreCase) && Name(left.Expression) is { } textOwner)
            {
                if (assignment.Right is LiteralExpressionSyntax literal && literal.Token.Value is string value)
                    _texts[textOwner] = value;
                else
                    _unsupportedTexts.Add(textOwner);
            }

            if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } ||
                invocation.Expression is not MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax collection } call ||
                !(collection.Name.Identifier.ValueText.Equals("Items", StringComparison.OrdinalIgnoreCase) ||
                    collection.Name.Identifier.ValueText.Equals("DropDownItems", StringComparison.OrdinalIgnoreCase))) continue;
            var parent = Name(collection.Expression);
            if (parent is null) continue;
            if (!_types.TryGetValue(parent, out var parentType) || !(IsItemType(parentType) ||
                parentType.EndsWith("MenuStrip", StringComparison.OrdinalIgnoreCase))) continue;
            var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
            if (arguments.Length != 1)
            {
                _unsupportedParents[parent] = Localizer.Get(StringKeys.WinFormsMenuOneConstantArgument);
                continue;
            }
            var expressions = call.Name.Identifier.ValueText.Equals("Add", StringComparison.OrdinalIgnoreCase)
                ? new[] { arguments[0].Expression }
                : arguments[0].Expression is ArrayCreationExpressionSyntax { Initializer: { } initializer }
                    ? initializer.Initializers.ToArray()
                    : null;
            if (expressions is null)
            {
                _unsupportedParents[parent] = Localizer.Get(StringKeys.WinFormsMenuConstantInitializationOnly);
                continue;
            }
            foreach (var expression in expressions)
            {
                if (Name(expression) is not { } child)
                {
                    _unsupportedParents[parent] = Localizer.Get(StringKeys.WinFormsMenuItemFieldReference);
                    break;
                }
                _links.Add((parent, child, statement));
            }
        }
    }

    /// <summary>Whether <paramref name="strip"/> and every one of its descendants can be read and edited safely.</summary>
    public bool IsSupported(string strip) => UnsupportedReason(strip) is null;

    /// <summary>Why <paramref name="strip"/> cannot be read, or null when it (and its descendants) are fully supported.</summary>
    public string? UnsupportedReason(string strip)
    {
        try
        {
            Read(strip);
            return null;
        }
        catch (NotSupportedException exception)
        {
            return exception.Message;
        }
    }

    public static bool IsItemType(string type) => new[]
        {
            "ToolStripMenuItem", "ToolStripSeparator", "System.Windows.Forms.ToolStripMenuItem", "System.Windows.Forms.ToolStripSeparator"
        }.Contains(type.Replace("Global.", "", StringComparison.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> DeclaredNames => _types.Keys.Concat(
        _class.DescendantNodes().Where(node => node.FirstAncestorOrSelf<ClassBlockSyntax>() == _class)
            .Select(node => node switch
            {
                MethodStatementSyntax method => method.Identifier.ValueText,
                PropertyStatementSyntax property => property.Identifier.ValueText,
                EventStatementSyntax @event => @event.Identifier.ValueText,
                DelegateStatementSyntax @delegate => @delegate.Identifier.ValueText,
                EnumStatementSyntax @enum => @enum.Identifier.ValueText,
                _ => null,
            }).OfType<string>());

    public IReadOnlyList<WinFormsMenuItem> Read(string strip)
    {
        var result = new List<WinFormsMenuItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string parent)
        {
            if (_unsupportedParents.TryGetValue(parent, out var reason)) throw new NotSupportedException(reason);
            foreach (var link in _links.Where(link => link.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!visited.Add(link.Child)) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsMenuItemsSharedOrCyclic));
                if (!_types.TryGetValue(link.Child, out var type) || !IsItemType(type))
                    throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsMenuCustomItemType));
                if (_unsupportedTexts.Contains(link.Child))
                    throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsMenuDynamicText));
                result.Add(new(link.Child, parent, _texts.GetValueOrDefault(link.Child) ?? "", type.EndsWith("ToolStripSeparator", StringComparison.OrdinalIgnoreCase)));
                Visit(link.Child);
            }
        }
        Visit(strip);
        return result;
    }

    public string Set(string strip, IReadOnlyList<WinFormsMenuItem> items)
    {
        var old = Read(strip);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!SyntaxFacts.IsValidIdentifier(item.Name) || !names.Add(item.Name))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuNamesMustBeUniqueIdentifiers));
            if (item.Text.Contains('\n') || item.Text.Contains('\r'))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuTextMustFitOnOneLine));
            if (DeclaredNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase) && !old.Any(previous => previous.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuNameAlreadyDeclared));
        }
        foreach (var item in items)
        {
            var ancestor = item;
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Name };
            while (!ancestor.Parent.Equals(strip, StringComparison.OrdinalIgnoreCase))
            {
                ancestor = items.SingleOrDefault(candidate => candidate.Name.Equals(ancestor.Parent, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuUnknownParent));
                if (ancestor.IsSeparator || !path.Add(ancestor.Name)) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuInvalidHierarchy));
            }
        }
        var changes = new List<TextChange>();
        var oldNames = old.Select(item => item.Name).Append(strip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in _links.Where(link => oldNames.Contains(link.Parent)).Select(link => link.Statement).Distinct())
            changes.Add(new(statement.Span, ""));
        var newline = _source.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var initializers = new List<string>();
        var fields = new List<string>();
        foreach (var item in items)
        {
            var previous = old.FirstOrDefault(candidate => candidate.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (previous is not null && previous.IsSeparator != item.IsSeparator)
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuItemCannotChangeType));
            if (previous is null)
            {
                var type = item.IsSeparator ? "ToolStripSeparator" : "ToolStripMenuItem";
                fields.Add($"    Friend WithEvents {item.Name} As Global.System.Windows.Forms.{type}");
                initializers.Add($"        Me.{item.Name} = New Global.System.Windows.Forms.{type}()");
                initializers.Add($"        Me.{item.Name}.Name = {Quote(item.Name)}");
            }
            var assignments = _initialize.Statements.OfType<AssignmentStatementSyntax>().Where(assignment =>
                assignment.Left is MemberAccessExpressionSyntax member && Name(member.Expression)?.Equals(item.Name, StringComparison.OrdinalIgnoreCase) == true &&
                member.Name.Identifier.ValueText.Equals("Text", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (assignments.LastOrDefault() is { } assignment)
                changes.Add(new(assignment.Right.Span, Quote(item.Text)));
            else if (!item.IsSeparator) initializers.Add($"        Me.{item.Name}.Text = {Quote(item.Text)}");
        }
        foreach (var parent in items.Select(item => item.Parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var collection = parent.Equals(strip, StringComparison.OrdinalIgnoreCase) ? "Items" : "DropDownItems";
            var children = string.Join(", ", items.Where(item => item.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase)).Select(item => "Me." + item.Name));
            initializers.Add($"        Me.{parent}.{collection}.AddRange(New Global.System.Windows.Forms.ToolStripItem() {{{children}}})");
        }
        // Detached fields remain intact so existing user event handlers and custom metadata stay valid.
        if (initializers.Count > 0)
            changes.Add(new(new TextSpan(WinFormsInitializeComponentSyntax.InsertionPoint(_initialize), 0), string.Join(newline, initializers) + newline));
        if (fields.Count > 0)
            changes.Add(new(new TextSpan(_class.EndClassStatement.FullSpan.Start, 0), string.Join(newline, fields) + newline));
        return _source.WithChanges(changes).ToString();
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static string? Name(ExpressionSyntax? expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member => member.Name.Identifier.ValueText,
        _ => null
    };
}
