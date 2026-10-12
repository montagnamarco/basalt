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
    private readonly IReadOnlyDictionary<string, ControlPlacement> _placements;
    private readonly IReadOnlySet<string> _items;

    /// <summary>What InitializeComponent does with a field it creates.</summary>
    private enum FieldRole { None, Control, Component }

    public WinFormsMenuDocument Menus { get; }
    public WinFormsToolStripDocument ToolStripItems { get; }

    /// <summary>
    /// Reads a designer file from its text alone, as Visual Studio's own output allows: a field that InitializeComponent
    /// adds to a Controls collection is a control, and any other field of a component type is a component of the tray.
    /// No type is loaded, so every construction site reads a file the same way.
    /// </summary>
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
        ToolStripItems = new WinFormsToolStripDocument(_text, _syntax);

        var fields = DeclaredNames;
        var (placements, items) = ReadCollections();
        _placements = placements;
        _items = items;

        var controls = new Dictionary<string, WinFormsDesignerControl>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Right is ObjectCreationExpressionSyntax creation
                && ControlName(assignment.Left) is { } name && fields.Contains(name)
                && ClassifyField(name, creation.Type.ToString()) == FieldRole.Control)
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

        foreach (var (child, placement) in placements)
        {
            if (!controls.TryGetValue(child, out var childControl)) continue;
            childControl.ParentName = placement.Parent;
            childControl.ParentPanel = placement.Panel;
            childControl.Cell = placement.Cell;
        }
        _components = ReadComponents();
        Controls = controls.Values.ToArray();
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
        parentName = ResolveParentKey(parentName);
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

    /// <summary>Where a control sits: its owner's name, the owner's panel when it is a container's panel, and its TableLayoutPanel cell.</summary>
    internal sealed record ControlPlacement(string Parent, string? Panel, (int Column, int Row)? Cell);

    /// <summary>
    /// What InitializeComponent adds to collections, keyed by the child's field name. A child of a <c>Controls</c> collection,
    /// or of a <c>TabPages</c> collection, is a control placed in its owner. A child of any other collection (Items,
    /// DropDownItems, Columns, Nodes, Groups, ColumnHeaders, ...) is an item of that collection, never a tray component.
    /// </summary>
    /// <remarks>
    /// The owner of <c>Me.SplitContainer1.Panel1.Controls</c> is SplitContainer1 with the panel Panel1, as Visual Studio writes a
    /// SplitContainer and a ToolStripContainer. <c>X.Controls.Add(Me.F, column, row)</c> takes the cell of a TableLayoutPanel,
    /// which is kept when both are integer literals.
    /// </remarks>
    private (Dictionary<string, ControlPlacement> Placements, HashSet<string> Items) ReadCollections()
    {
        var placements = new Dictionary<string, ControlPlacement>(StringComparer.OrdinalIgnoreCase);
        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var invocation in _initialize.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax collection } call)
                continue;
            var method = call.Name.Identifier.ValueText;
            var isAdd = method.Equals("Add", StringComparison.OrdinalIgnoreCase);
            if (!isAdd && !method.Equals("AddRange", StringComparison.OrdinalIgnoreCase)) continue;
            var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
            IEnumerable<ExpressionSyntax> children = isAdd && arguments.Length >= 1 ? [arguments[0].Expression]
                : !isAdd && arguments.Length == 1 && arguments[0].Expression is ArrayCreationExpressionSyntax { Initializer: { } initializer }
                    ? initializer.Initializers : [];
            var collectionName = collection.Name.Identifier.ValueText;
            var isControls = collectionName.Equals("Controls", StringComparison.OrdinalIgnoreCase)
                || collectionName.Equals("TabPages", StringComparison.OrdinalIgnoreCase);
            var (parent, panel) = OwnerOf(collection.Expression);
            var cell = isAdd && arguments.Length == 3 && arguments[1].Expression is LiteralExpressionSyntax { Token.Value: int column }
                && arguments[2].Expression is LiteralExpressionSyntax { Token.Value: int row }
                ? ((int Column, int Row)?)(column, row) : null;
            foreach (var child in children)
            {
                if (ControlName(child) is not { } childName) continue;
                if (isControls) placements[childName] = new ControlPlacement(parent, panel, cell);
                else items.Add(childName);
            }
        }
        return (placements, items);
    }

    /// <summary>The owner of a Controls collection: the control it belongs to, and the panel of a container that it is; "Me" for the form.</summary>
    private (string Parent, string? Panel) OwnerOf(ExpressionSyntax expression)
    {
        if (ControlName(expression) is { } name) return (name, null);
        if (expression is MemberAccessExpressionSyntax { Expression: var inner } panel && ControlName(inner) is { } owner)
            return (owner, panel.Name.Identifier.ValueText);
        return ("Me", null);
    }

    /// <summary>
    /// The role of a field that InitializeComponent creates. The components container is neither, and neither is an item of
    /// a collection (see <see cref="ReadCollections"/>) or a menu, ToolStrip or grid item, which its own document owns. A
    /// Forms tray type is a component. Otherwise a field that a Controls collection adds is a control, and any other field
    /// is a component: how Visual Studio writes a non-visual class of the project.
    /// </summary>
    private FieldRole ClassifyField(string name, string typeName)
    {
        if (IsDesignerContainerTypeName(typeName) || IsNonVisualItemTypeName(typeName) || _items.Contains(name)) return FieldRole.None;
        if (IsTrayTypeName(typeName)) return FieldRole.Component;
        return _placements.ContainsKey(name) ? FieldRole.Control : FieldRole.Component;
    }

    private static bool IsNonVisualItemTypeName(string typeName) =>
        WinFormsMenuDocument.IsItemType(typeName) || IsToolStripItemTypeName(typeName)
        || WinFormsCollectionDocument.ColumnTypes.Any(type =>
            typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).Equals(type, StringComparison.OrdinalIgnoreCase)
            || typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase).Equals("System.Windows.Forms." + type, StringComparison.OrdinalIgnoreCase));

    private string NewLine => Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>
    /// The key of a parent as the designer names it: a control, or a container's panel as Owner.Panel (SplitContainer1.Panel1).
    /// The owner must be a control of the form; the panel is kept as the designer's code names it.
    /// </summary>
    private string ResolveParentKey(string key)
    {
        var dot = key.IndexOf('.');
        var owner = FindControl(dot < 0 ? key : key[..dot]).Name;
        return dot < 0 ? owner : owner + "." + key[(dot + 1)..];
    }

    private WinFormsDesignerControl FindControl(string name) => name.Equals("Me", StringComparison.OrdinalIgnoreCase)
        ? Root : Controls.FirstOrDefault(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerControlNotFound), nameof(name));

    private static void ValidateIdentifier(string value)
    {
        if (!SyntaxFacts.IsValidIdentifier(value)) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerIdentifierRequired));
    }

    private static bool IsToolStripItemTypeName(string typeName) => WinFormsToolStripDocument.KnownItemTypes.Contains(
        typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("System.Windows.Forms.", "", StringComparison.OrdinalIgnoreCase),
        StringComparer.OrdinalIgnoreCase);

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
    /// <summary>The container's panel that holds the control, such as Panel1 of a SplitContainer; null when the owner holds it.</summary>
    public string? ParentPanel { get; internal set; }
    /// <summary>The TableLayoutPanel cell of the control, when the designer's code gives its column and row as integer literals.</summary>
    public (int Column, int Row)? Cell { get; internal set; }
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ExpressionSyntax> PropertySyntax { get; } = new(StringComparer.OrdinalIgnoreCase);
}
