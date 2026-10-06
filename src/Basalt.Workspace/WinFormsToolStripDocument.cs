using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>One item directly owned by a ToolStrip or StatusStrip's own Items collection.</summary>
public sealed record WinFormsToolStripItem(string Name, string TypeName, string Text);

/// <summary>
/// Reads and edits the flat Items collection a ToolStrip or StatusStrip owns directly, the
/// shape Visual Studio's "Type Here" box edits. Unlike <see cref="WinFormsMenuDocument"/> this
/// never follows DropDownItems: a DropDownButton or SplitButton added here keeps its own
/// drop-down menu, which is edited the same way a MenuStrip is, not through this document.
/// </summary>
public sealed partial class WinFormsToolStripDocument
{
    /// <summary>Display name shown in the "Type Here" drop-down, mapped to the WinForms type it creates.</summary>
    public static readonly (string Display, string TypeName)[] ToolStripItemKinds =
    [
        ("Button", "ToolStripButton"),
        ("Label", "ToolStripLabel"),
        ("SplitButton", "ToolStripSplitButton"),
        ("DropDownButton", "ToolStripDropDownButton"),
        ("Separator", "ToolStripSeparator"),
        ("ComboBox", "ToolStripComboBox"),
        ("TextBox", "ToolStripTextBox"),
        ("ProgressBar", "ToolStripProgressBar"),
    ];

    public static readonly (string Display, string TypeName)[] StatusStripItemKinds =
    [
        ("StatusLabel", "ToolStripStatusLabel"),
        ("ProgressBar", "ToolStripProgressBar"),
        ("DropDownButton", "ToolStripDropDownButton"),
        ("SplitButton", "ToolStripSplitButton"),
    ];

    /// <summary>The type "Type Here" creates when text is typed directly, without opening the drop-down.</summary>
    public static string DefaultTypeName(bool statusStrip) => statusStrip ? "ToolStripStatusLabel" : "ToolStripButton";

    private static readonly string[] SeparatorTypes = ["ToolStripSeparator"];

    // ComboBox/TextBox/ProgressBar host a control instead of painting text; Visual Studio
    // never emits a Text assignment for them, so round-tripping one must not invent one either.
    private static readonly string[] TextlessTypes =
    [
        "ToolStripSeparator", "ToolStripComboBox", "ToolStripTextBox", "ToolStripProgressBar",
    ];

    public static readonly string[] KnownItemTypes =
        ToolStripItemKinds.Concat(StatusStripItemKinds).Select(kind => kind.TypeName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private readonly SourceText _source;
    private readonly ClassBlockSyntax _class;
    private readonly MethodBlockSyntax _initialize;
    private readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsupportedTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Strip, string Item, StatementSyntax Statement)> _links = [];
    private readonly Dictionary<string, string> _unsupportedStrips = new(StringComparer.OrdinalIgnoreCase);

    public WinFormsToolStripDocument(string source)
        : this(SourceText.From(source), VisualBasicSyntaxTree.ParseText(source).GetRoot())
    {
    }

    internal WinFormsToolStripDocument(SourceText source, SyntaxNode root)
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
                !collection.Name.Identifier.ValueText.Equals("Items", StringComparison.OrdinalIgnoreCase)) continue;
            var strip = Name(collection.Expression);
            if (strip is null) continue;
            if (!_types.TryGetValue(strip, out var stripType) || !IsStripType(stripType)) continue;
            var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
            if (arguments.Length != 1)
            {
                _unsupportedStrips[strip] = Localizer.Get(StringKeys.WinFormsToolStripOneConstantArgument);
                continue;
            }
            var expressions = call.Name.Identifier.ValueText.Equals("Add", StringComparison.OrdinalIgnoreCase)
                ? new[] { arguments[0].Expression }
                : arguments[0].Expression is ArrayCreationExpressionSyntax { Initializer: { } initializer }
                    ? initializer.Initializers.ToArray()
                    : null;
            if (expressions is null)
            {
                _unsupportedStrips[strip] = Localizer.Get(StringKeys.WinFormsToolStripConstantInitializationOnly);
                continue;
            }
            foreach (var expression in expressions)
            {
                if (Name(expression) is not { } item)
                {
                    _unsupportedStrips[strip] = Localizer.Get(StringKeys.WinFormsToolStripItemFieldReference);
                    break;
                }
                _links.Add((strip, item, statement));
            }
        }
    }

    public bool IsSupported(string strip) => UnsupportedReason(strip) is null;

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

    public static bool IsStripType(string type) =>
        type.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).EndsWith("ToolStrip", StringComparison.OrdinalIgnoreCase) ||
        type.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).EndsWith("StatusStrip", StringComparison.OrdinalIgnoreCase);

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

    public IReadOnlyList<WinFormsToolStripItem> Read(string strip)
    {
        if (_unsupportedStrips.TryGetValue(strip, out var reason)) throw new NotSupportedException(reason);
        var result = new List<WinFormsToolStripItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in _links.Where(link => link.Strip.Equals(strip, StringComparison.OrdinalIgnoreCase)))
        {
            if (!visited.Add(link.Item)) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsToolStripItemsSharedOrCyclic));
            if (!_types.TryGetValue(link.Item, out var type))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsToolStripCustomItemType));
            var bare = type.Replace("Global.", "", StringComparison.OrdinalIgnoreCase)
                .Replace("System.Windows.Forms.", "", StringComparison.OrdinalIgnoreCase);
            if (!KnownItemTypes.Contains(bare, StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsToolStripCustomItemType));
            if (_unsupportedTexts.Contains(link.Item))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsToolStripDynamicText));
            result.Add(new(link.Item, bare, _texts.GetValueOrDefault(link.Item) ?? ""));
        }
        return result;
    }

    public string Set(string strip, IReadOnlyList<WinFormsToolStripItem> items)
    {
        var old = Read(strip);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!SyntaxFacts.IsValidIdentifier(item.Name) || !names.Add(item.Name))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsToolStripNamesMustBeUniqueIdentifiers));
            if (item.Text.Contains('\n') || item.Text.Contains('\r'))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsToolStripTextMustFitOnOneLine));
            if (DeclaredNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase) && !old.Any(previous => previous.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsToolStripNameAlreadyDeclared));
            var previous = old.FirstOrDefault(candidate => candidate.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (previous is not null && !previous.TypeName.Equals(item.TypeName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsToolStripItemCannotChangeType));
        }

        var changes = new List<TextChange>();
        var oldNames = old.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in _links.Where(link => link.Strip.Equals(strip, StringComparison.OrdinalIgnoreCase) && oldNames.Contains(link.Item))
            .Select(link => link.Statement).Distinct())
            changes.Add(new(statement.Span, ""));

        var newline = _source.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var initializers = new List<string>();
        var fields = new List<string>();
        foreach (var item in items)
        {
            var previous = old.FirstOrDefault(candidate => candidate.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (previous is null)
            {
                fields.Add($"    Friend WithEvents {item.Name} As Global.System.Windows.Forms.{item.TypeName}");
                initializers.Add($"        Me.{item.Name} = New Global.System.Windows.Forms.{item.TypeName}()");
                initializers.Add($"        Me.{item.Name}.Name = {Quote(item.Name)}");
            }
            var assignments = _initialize.Statements.OfType<AssignmentStatementSyntax>().Where(assignment =>
                assignment.Left is MemberAccessExpressionSyntax member && Name(member.Expression)?.Equals(item.Name, StringComparison.OrdinalIgnoreCase) == true &&
                member.Name.Identifier.ValueText.Equals("Text", StringComparison.OrdinalIgnoreCase)).ToArray();
            var textless = TextlessTypes.Contains(item.TypeName, StringComparer.OrdinalIgnoreCase);
            if (assignments.LastOrDefault() is { } assignment)
                changes.Add(new(assignment.Right.Span, Quote(item.Text)));
            else if (!textless && item.Text.Length > 0) initializers.Add($"        Me.{item.Name}.Text = {Quote(item.Text)}");
        }

        if (items.Count > 0)
        {
            var children = string.Join(", ", items.Select(item => "Me." + item.Name));
            initializers.Add($"        Me.{strip}.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {{{children}}})");
        }

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
        _ => null,
    };
}
