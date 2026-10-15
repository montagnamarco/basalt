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
        ThrowOnSyntaxErrors(_syntax);
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
        var statement = $"{receiver}.{propertyName} = {expression}";
        // A property that selects a child, such as a TabControl's SelectedIndex, needs the child to exist: it goes after the
        // last child is added, as Visual Studio writes it. The pages of a TabControl are added after its own Controls.Add.
        if (NeedsChildrenFirst(propertyName) && LastChildAddStatement(controlName) is { } lastChild)
            return InsertStatementAfter(lastChild, statement);
        // Visual Studio writes a control's properties before its Controls.Add, so a new one goes in front of it.
        return ControlAddStatement(controlName) is { } add
            ? InsertStatementBefore(add, statement)
            : InsertStatements(statement);
    }

    /// <summary>
    /// Adds a control the way Visual Studio writes one: its properties first, then <c>Controls.Add</c>. The starting size,
    /// text and dock come from <see cref="WinFormsControlDefaults"/>, the TabIndex is the next one in drop order, and a
    /// TabControl brings its two TabPages, which are controls of their own.
    /// </summary>
    /// <param name="location">Where the control was dropped, in its parent's client area; null keeps the control at (20, 20).</param>
    public string AddControl(string name, string typeName, string parentName = "Me", (int X, int Y)? location = null)
    {
        ValidateIdentifier(name);
        parentName = ResolveParentKey(parentName);
        if (Controls.Any(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            || Components.Any(component => component.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerNameAlreadyExists), nameof(name));
        var type = SyntaxFactory.ParseTypeName(typeName);
        if (type.ContainsDiagnostics) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerControlTypeInvalid));

        var starting = WinFormsControlDefaults.For(typeName);
        var tabIndex = NextTabIndex(parentName);
        var form = FormClientSize();
        var statements = new List<string>();
        var fields = new List<string>();
        AppendControl(statements, fields, name, typeName, starting, location ?? (20, 20), form, tabIndex,
            parentName == "Me" ? "Me" : $"Me.{parentName}");
        if (starting.TabPages)
        {
            var pageType = "Global.System.Windows.Forms.TabPage";
            var pages = FreeNames("TabPage", 2, name);
            // The pages are children of the TabControl, so they are numbered within it from 0, not after the form's controls.
            for (var index = 0; index < pages.Length; index++)
                AppendControl(statements, fields, pages[index], pageType, new(Text: true), null, form, index, $"Me.{name}");
        }
        var source = InsertStatements(statements.ToArray());
        var updated = new WinFormsDesignerDocument(source);
        var position = updated._class.EndClassStatement.FullSpan.Start;
        return updated._text.WithChanges(new TextChange(new TextSpan(position, 0),
            string.Concat(fields.Select(field => $"    {field}{NewLine}")))).ToString();
    }

    /// <summary>
    /// The statements that create one control, in Visual Studio's order: New, Name, Location, Size, AutoSize, Text, Dock,
    /// TabIndex, and last <c>Controls.Add</c>. The Location and Size come from <see cref="Bounds"/>.
    /// </summary>
    private static void AppendControl(List<string> statements, List<string> fields, string name, string typeName,
        WinFormsControlDefaults.Starting starting, (int X, int Y)? location, (int Width, int Height) form, int tabIndex,
        string parentReceiver)
    {
        statements.Add($"Me.{name} = New {typeName}()");
        statements.Add($"Me.{name}.Name = \"{name}\"");
        var (position, size) = Bounds(starting, location, form);
        if (position is { } point)
            statements.Add($"Me.{name}.Location = New System.Drawing.Point({point.X}, {point.Y})");
        if (size is { } measure)
            statements.Add($"Me.{name}.Size = New System.Drawing.Size({measure.Width}, {measure.Height})");
        if (starting.AutoSize) statements.Add($"Me.{name}.AutoSize = True");
        if (starting.Text) statements.Add($"Me.{name}.Text = \"{name}\"");
        if (starting.Dock is { } dock) statements.Add($"Me.{name}.Dock = System.Windows.Forms.DockStyle.{dock}");
        statements.Add($"Me.{name}.TabIndex = {tabIndex}");
        statements.Add($"{parentReceiver}.Controls.Add(Me.{name})");
        fields.Add($"Friend WithEvents {name} As {typeName}");
    }

    /// <summary>
    /// The next TabIndex in drop order within a container: one more than the highest that a control of the same container
    /// already has, or 0. Visual Studio numbers the tab order per container, so a TabPage's index does not follow the form's.
    /// </summary>
    private int NextTabIndex(string containerKey) => Controls
        .Where(control => string.Equals(ContainerKeyOf(control), containerKey, StringComparison.OrdinalIgnoreCase))
        .Select(control => control.Properties.TryGetValue("TabIndex", out var value)
            && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? index : -1)
        .DefaultIfEmpty(-1)
        .Max() + 1;

    /// <summary>The key of the container a control sits in, as <see cref="ResolveParentKey"/> names it: its owner, or owner.panel.</summary>
    private static string ContainerKeyOf(WinFormsDesignerControl control) =>
        control.ParentPanel is null ? control.ParentName : control.ParentName + "." + control.ParentPanel;

    /// <summary>
    /// The Location and Size a new control is written with. A control that is not docked takes the drop point and its
    /// starting size. A docked control takes none, except a strip (MenuStrip, ToolStrip, StatusStrip), which Visual Studio
    /// writes at its docked edge across the form's client width.
    /// </summary>
    private static ((int X, int Y)? Position, (int Width, int Height)? Size) Bounds(
        WinFormsControlDefaults.Starting starting, (int X, int Y)? location, (int Width, int Height) form)
    {
        if (starting.Dock is null)
        {
            (int Width, int Height)? size = starting.Width is { } width && starting.Height is { } height ? (width, height) : null;
            return (location, size);
        }
        if (!starting.DockedBounds) return (null, null);
        var stripHeight = starting.Height ?? 0;
        var top = starting.Dock == "Bottom" ? form.Height - stripHeight : 0;
        return ((0, top), (form.Width, stripHeight));
    }

    /// <summary>The form's client size as its designer code writes it, or Visual Studio's default for a form that writes none.</summary>
    private (int Width, int Height) FormClientSize()
    {
        var arguments = Root.PropertySyntax.TryGetValue("ClientSize", out var expression)
            && expression is ObjectCreationExpressionSyntax creation
            ? creation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().Select(argument => argument.Expression).ToArray()
            : null;
        return arguments is [LiteralExpressionSyntax { Token.Value: int width }, LiteralExpressionSyntax { Token.Value: int height }]
            ? (width, height) : (300, 300);
    }

    /// <summary>
    /// The first <paramref name="count"/> names of the form <c>prefix</c> + number that no field of the designer uses, and
    /// that <paramref name="alsoTaken"/> does not name either: the control being added is not a field yet.
    /// </summary>
    private string[] FreeNames(string prefix, int count, string alsoTaken)
    {
        var taken = DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        taken.Add(alsoTaken);
        var names = new List<string>();
        for (var number = 1; names.Count < count; number++)
            if (!taken.Contains(prefix + number)) names.Add(prefix + number);
        return names.ToArray();
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

    /// <summary>The <c>Controls.Add(Me.name)</c> statement of a control in InitializeComponent, the one that places it, or null.</summary>
    private StatementSyntax? ControlAddStatement(string name) => _initialize.Statements.OfType<ExpressionStatementSyntax>()
        .FirstOrDefault(statement => IsControlsAddOf(statement.Expression, name));

    private static bool IsControlsAddOf(ExpressionSyntax expression, string name) =>
        expression is InvocationExpressionSyntax invocation
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Add",
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Controls" },
        }
        && invocation.ArgumentList is { } list
        && list.Arguments.OfType<SimpleArgumentSyntax>().Any(argument => argument.Expression is MemberAccessExpressionSyntax
        {
            Expression: MeExpressionSyntax,
        } member && member.Name.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase));

    private string InsertStatementBefore(StatementSyntax anchor, string statement) =>
        _text.WithChanges(new TextChange(new TextSpan(anchor.SpanStart, 0), $"{statement}{NewLine}        ")).ToString();

    private string InsertStatementAfter(StatementSyntax anchor, string statement) =>
        _text.WithChanges(new TextChange(new TextSpan(anchor.Span.End, 0), $"{NewLine}        {statement}")).ToString();

    /// <summary>The properties that select one of a control's children, which Visual Studio writes after the children are added.</summary>
    private static bool NeedsChildrenFirst(string propertyName) =>
        propertyName.Equals("SelectedIndex", StringComparison.OrdinalIgnoreCase)
        || propertyName.Equals("SelectedTab", StringComparison.OrdinalIgnoreCase);

    /// <summary>The last <c>Me.name.Controls.Add(...)</c> statement of InitializeComponent, the one that adds a child of the control, or null.</summary>
    private StatementSyntax? LastChildAddStatement(string name) => _initialize.Statements.OfType<ExpressionStatementSyntax>()
        .LastOrDefault(statement => AddsToControlsOf(statement.Expression, name));

    private static bool AddsToControlsOf(ExpressionSyntax expression, string name) =>
        expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add", Expression: MemberAccessExpressionSyntax controls },
        }
        && (controls.Name.Identifier.ValueText.Equals("Controls", StringComparison.OrdinalIgnoreCase)
            || controls.Name.Identifier.ValueText.Equals("TabPages", StringComparison.OrdinalIgnoreCase))
        && string.Equals(ControlName(controls.Expression), name, StringComparison.OrdinalIgnoreCase);

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
