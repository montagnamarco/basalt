using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Reads and edits the designer method without executing application code.</summary>
public sealed partial class WinFormsDesignerDocument
{
    private readonly CompilationUnitSyntax _syntax;
    private readonly ClassBlockSyntax _class;
    private readonly MethodBlockSyntax _initialize;
    private readonly SourceText _text;

    public WinFormsMenuDocument Menus { get; }

    public WinFormsDesignerDocument(string source)
    {
        _text = SourceText.From(source);
        _syntax = (CompilationUnitSyntax)VisualBasicSyntaxTree.ParseText(_text).GetRoot();
        if (_syntax.ContainsDiagnostics)
            throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerSyntaxErrors));
        _initialize = _syntax.DescendantNodes().OfType<MethodBlockSyntax>()
            .SingleOrDefault(method => method.SubOrFunctionStatement.Identifier.ValueText
                .Equals("InitializeComponent", StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerNoInitializeComponent));
        _class = _initialize.FirstAncestorOrSelf<ClassBlockSyntax>()!;
        ClassName = _class.ClassStatement.Identifier.ValueText;
        Menus = new WinFormsMenuDocument(_text, _syntax);

        var controls = new Dictionary<string, WinFormsDesignerControl>(StringComparer.OrdinalIgnoreCase);
        var fields = DeclaredNames;
        foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Right is ObjectCreationExpressionSyntax creation
                && !IsTrayTypeName(creation.Type.ToString())
                && !IsDesignerContainerTypeName(creation.Type.ToString())
                && ControlName(assignment.Left) is { } name && fields.Contains(name))
                controls[name] = new(name, creation.Type.ToString());
        }

        Root = new("Me", "System.Windows.Forms.Form");
        foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax member) continue;
            var name = ControlName(member.Expression);
            var target = name is null or "Me" ? Root : controls.GetValueOrDefault(name);
            if (target is not null)
            {
                // All expressions are retained as syntax; the preview accepts only safe constants.
                if (!controls.ContainsKey(member.Name.Identifier.ValueText))
                {
                    target.Properties[member.Name.Identifier.ValueText] = assignment.Right.ToString();
                    target.PropertySyntax[member.Name.Identifier.ValueText] = assignment.Right;
                }
            }
        }

        foreach (var invocation in _initialize.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression:
                    MemberAccessExpressionSyntax collection
                } call
                || !collection.Name.Identifier.ValueText.Equals("Controls", StringComparison.OrdinalIgnoreCase))
                continue;
            var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
            if (arguments.Length != 1) continue;
            var method = call.Name.Identifier.ValueText;
            IEnumerable<ExpressionSyntax> children;
            if (method.Equals("Add", StringComparison.OrdinalIgnoreCase))
                children = [arguments[0].Expression];
            else if (method.Equals("AddRange", StringComparison.OrdinalIgnoreCase)
                && arguments[0].Expression is ArrayCreationExpressionSyntax { Initializer: { } initializer })
                children = initializer.Initializers;
            else
                continue;

            foreach (var expression in children)
            {
                if (ControlName(expression) is { } childName && controls.TryGetValue(childName, out var childControl))
                    childControl.ParentName = ControlName(collection.Expression) ?? "Me";
            }
        }
        _components = ReadComponents();
        Controls = controls.Values.Where(control => !WinFormsMenuDocument.IsItemType(control.TypeName) && !WinFormsCollectionDocument.ColumnTypes.Any(type =>
            control.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).Equals(type, StringComparison.OrdinalIgnoreCase)
            || control.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).Equals("System.Windows.Forms." + type, StringComparison.OrdinalIgnoreCase))).ToArray();
        ResourceOperations = ReadResourceOperations();
        Collections = new WinFormsCollectionDocument(Source, _syntax);
        ValidateControlHierarchy();
        ZOrder = ReadZOrder();
    }

    public string Source => _text.ToString();
    public string ClassName { get; }
    public WinFormsDesignerControl Root { get; }
    public IReadOnlyList<WinFormsDesignerControl> Controls { get; }

    public string SetProperty(string controlName, string propertyName, string? expression)
    {
        ValidateIdentifier(propertyName);
        FindControl(controlName);
        var matches = _initialize.Statements.OfType<AssignmentStatementSyntax>()
            .Where(statement => statement.Left is MemberAccessExpressionSyntax member
                && string.Equals(ControlName(member.Expression), controlName, StringComparison.OrdinalIgnoreCase)
                && member.Name.Identifier.ValueText.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (expression is null)
            return _text.WithChanges(matches.Select(statement => new TextChange(statement.FullSpan, ""))).ToString();
        var parsed = SyntaxFactory.ParseExpression(expression);
        if (parsed.ContainsDiagnostics) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerInvalidExpression));
        if (matches.Length != 0)
        {
            // Earlier assignments can be resource lookups or application setup.
            // Only the effective final assignment is changed by a property edit.
            return _text.WithChanges(new TextChange(matches[^1].Right.Span, expression)).ToString();
        }
        var receiver = controlName.Equals("Me", StringComparison.OrdinalIgnoreCase) ? "Me" : $"Me.{controlName}";
        return InsertStatements($"{receiver}.{propertyName} = {expression}");
    }

    public string AddControl(string name, string typeName, string parentName = "Me")
    {
        ValidateIdentifier(name);
        FindControl(parentName);
        if (Controls.Any(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            || Components.Any(component => component.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerNameAlreadyExists), nameof(name));
        var type = SyntaxFactory.ParseTypeName(typeName);
        if (type.ContainsDiagnostics) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerControlTypeInvalid));
        var source = InsertStatements($"Me.{name} = New {typeName}()",
            $"Me.{name}.Name = \"{name}\"", $"Me.{name}.Location = New System.Drawing.Point(20, 20)",
            $"Me.{name}.Size = New System.Drawing.Size(100, 30)", $"Me.{name}.Text = \"{name}\"",
            $"{(parentName == "Me" ? "Me" : $"Me.{parentName}")}.Controls.Add(Me.{name})");
        var updated = new WinFormsDesignerDocument(source);
        var position = updated._class.EndClassStatement.FullSpan.Start;
        return updated._text.WithChanges(new TextChange(new TextSpan(position, 0),
            $"    Friend WithEvents {name} As {typeName}{NewLine}")).ToString();
    }

    public string RemoveControl(string name)
    {
        if (name.Equals("Me", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRootCannotBeDeleted));
        FindControl(name);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        bool changed;
        do
        {
            changed = false;
            foreach (var control in Controls)
                if (removed.Contains(control.ParentName) && removed.Add(control.Name)) changed = true;
        } while (changed);
        var changes = new List<TextChange>();
        foreach (var statement in _initialize.Statements)
            if (statement.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
                .Any(member => member.Expression is MeExpressionSyntax && removed.Contains(member.Name.Identifier.ValueText)))
                changes.Add(new(statement.FullSpan, ""));
        foreach (var field in _class.Members.OfType<FieldDeclarationSyntax>())
        {
            var names = field.Declarators.SelectMany(declarator => declarator.Names).ToArray();
            if (names.Any(identifier => removed.Contains(identifier.Identifier.ValueText)))
            {
                if (!names.All(identifier => removed.Contains(identifier.Identifier.ValueText)))
                    throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerSplitSharedFieldDeleteControl));
                changes.Add(new(field.FullSpan, ""));
            }
        }
        foreach (var declaration in _class.DescendantNodes().OfType<MethodStatementSyntax>())
        {
            if (declaration.HandlesClause is not { } handles) continue;
            var remaining = handles.Events.Where(item => !removed.Contains(item.EventContainer.ToString())).ToArray();
            if (remaining.Length == handles.Events.Count) continue;
            changes.Add(new(handles.Span, remaining.Length == 0 ? ""
                : handles.WithEvents(SyntaxFactory.SeparatedList(remaining)).ToString()));
        }
        return _text.WithChanges(changes).ToString();
    }

    private string InsertStatements(params string[] statements)
    {
        var position = WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);
        return _text.WithChanges(new TextChange(new TextSpan(position, 0),
            string.Join("", statements.Select(statement => $"        {statement}{NewLine}")))).ToString();
    }

    private string NewLine => Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private WinFormsDesignerControl FindControl(string name) => name.Equals("Me", StringComparison.OrdinalIgnoreCase)
        ? Root : Controls.FirstOrDefault(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerControlNotFound), nameof(name));

    private static void ValidateIdentifier(string value)
    {
        if (!SyntaxFacts.IsValidIdentifier(value)) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerIdentifierRequired));
    }

    private static string? ControlName(ExpressionSyntax expression) => expression switch
    {
        MeExpressionSyntax => "Me",
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member => member.Name.Identifier.ValueText,
        _ => null,
    };
}

public sealed record WinFormsDesignerControl(string Name, string TypeName)
{
    public string ParentName { get; internal set; } = "Me";
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ExpressionSyntax> PropertySyntax { get; } = new(StringComparer.OrdinalIgnoreCase);
}
