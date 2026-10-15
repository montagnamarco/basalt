using System.Collections.Immutable;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>An in-process snapshot of designer controls and their parsed initialization statements.</summary>
public sealed class WinFormsControlClipboard
{
    internal WinFormsControlClipboard(ImmutableArray<WinFormsClipboardControl> controls,
        ImmutableArray<WinFormsClipboardAuxiliary> auxiliaries,
        ImmutableArray<StatementSyntax> statements, ImmutableArray<string> topLevelNames,
        ImmutableDictionary<string, string?> requiredFields,
        ImmutableHashSet<string> requiredMethods,
        ImmutableArray<WinFormsClipboardResource> resources)
    {
        Controls = controls;
        Auxiliaries = auxiliaries;
        Statements = statements;
        TopLevelNames = topLevelNames;
        RequiredFields = requiredFields;
        RequiredMethods = requiredMethods;
        Resources = resources;
    }

    internal ImmutableArray<WinFormsClipboardControl> Controls { get; }
    internal ImmutableArray<WinFormsClipboardAuxiliary> Auxiliaries { get; }
    internal ImmutableArray<StatementSyntax> Statements { get; }
    internal ImmutableDictionary<string, string?> RequiredFields { get; }
    internal ImmutableHashSet<string> RequiredMethods { get; }
    internal ImmutableArray<WinFormsClipboardResource> Resources { get; }

    public IReadOnlyList<string> TopLevelNames { get; }
}

internal sealed record WinFormsClipboardControl(string Name, string TypeName, string ParentName, string? ParentPanel = null,
    (int Column, int Row)? Cell = null);
internal sealed record WinFormsClipboardAuxiliary(string Name, string TypeName);

/// <summary>
/// A resx entry a copied statement reads through GetObject or GetString, with the copied control that reads it. A picture
/// is one of them: it stays a GetObject call in code, so the paste needs the stored entry under the key it now reads.
/// </summary>
internal sealed record WinFormsClipboardResource(string Owner, string Key, WinFormsResourceEntry Entry);

public sealed record WinFormsControlPasteResult(string Source,
    IReadOnlyList<string> TopLevelNames, IReadOnlyList<string> AllNames, IReadOnlyList<WinFormsResourceEntry> Resources);

public sealed partial class WinFormsDesignerDocument
{
    /// <summary>
    /// Copies the selected controls of a form whose values are all in code. A copied ApplyResources call is refused, as
    /// it reads a neutral .resx this method does not have; use the overload with the resolved values for a Localizable form.
    /// </summary>
    public WinFormsControlClipboard CopyControls(IEnumerable<string> selectedNames) =>
        CopyControls(selectedNames, localizedValues: null);

    /// <summary>
    /// Copies the selected controls. <paramref name="localizedValues"/> are the values a Localizable form's ApplyResources
    /// calls read from its neutral .resx, resolved by the caller. Each copied ApplyResources call is replaced by literal
    /// assignments of its control's values, so the fragment pastes into a form that is not localizable, and a Localizable
    /// form pastes it through its own localizable path. Null means the caller did not resolve them, so a copied
    /// ApplyResources call is refused. <paramref name="resources"/> is the form's own .resx: the entries the copied
    /// GetObject and GetString reads name are carried with the fragment, so a picture pastes with its bytes.
    /// <paramref name="carriedEntries"/> names further entries of <paramref name="resources"/> to carry, the ones a
    /// read-only property owns; an entry whose owner is not copied is left out.
    /// </summary>
    public WinFormsControlClipboard CopyControls(IEnumerable<string> selectedNames,
        IReadOnlyCollection<WinFormsLocalizedAssignment>? localizedValues, WinFormsResourceDocument? resources = null,
        IReadOnlyCollection<string>? carriedEntries = null)
    {
        ArgumentNullException.ThrowIfNull(selectedNames);
        var selected = selectedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length == 0) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerSelectAtLeastOneControl), nameof(selectedNames));
        foreach (var name in selected)
        {
            if (name.Equals("Me", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRootCannotBeCopied), nameof(selectedNames));
            FindControl(name);
        }

        var copied = Controls.Where(control => IsSelectedOrDescendant(control, selected)).ToArray();
        var names = copied.Select(control => control.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var topLevel = selected.Where(name => copied.Any(control =>
            control.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && !names.Contains(control.ParentName))).ToImmutableArray();
        var controlStatements = CaptureInitializationStatements(names, localizedValues);
        var auxiliaries = controlStatements.SelectMany(ReferencedFields)
            .Where(name => !names.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ColumnAuxiliary(name))
            .OfType<WinFormsClipboardAuxiliary>()
            .ToImmutableArray();
        foreach (var auxiliary in auxiliaries) names.Add(auxiliary.Name);
        var statements = CaptureInitializationStatements(names, localizedValues);
        var requiredFields = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        var requiredMethods = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in statements)
        {
            foreach (var field in ReferencedFields(statement))
            {
                if (!names.Contains(field) && DeclaredNames.Contains(field))
                    requiredFields[field] = FieldType(field);
            }

            foreach (var method in ReferencedLocalMethods(statement))
                requiredMethods.Add(method);
        }

        return new WinFormsControlClipboard(
            copied.Select(control => new WinFormsClipboardControl(
                control.Name, control.TypeName, control.ParentName, control.ParentPanel, control.Cell)).ToImmutableArray(),
            auxiliaries,
            statements, topLevel, requiredFields.ToImmutable(), requiredMethods.ToImmutable(),
            CopiedResources(statements, resources, carriedEntries ?? [], names));
    }

    /// <summary>
    /// Pastes the copied controls into <paramref name="targetParent"/>. With <paramref name="gridStep"/>, a copy that goes back
    /// into the container it was copied from is offset from its original by whole steps, as Visual Studio offsets a paste on
    /// the same form: the fewest steps that put no pasted control on a location in <paramref name="occupied"/> and keep it inside
    /// <paramref name="clientSize"/>, wrapping when none does. Without a grid step, or
    /// into another container, a copy keeps the location it was copied with.
    /// </summary>
    public WinFormsControlPasteResult PasteControls(WinFormsControlClipboard clipboard, string targetParent,
        (int Width, int Height)? gridStep = null, IReadOnlyCollection<(int X, int Y)>? occupied = null,
        (int Width, int Height)? clientSize = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (gridStep is { } step)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(step.Width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(step.Height, 1);
        }
        targetParent = ResolveParentKey(targetParent);
        ValidateClipboardDependencies(clipboard);

        string? managerName = null;
        if (clipboard.Statements.Any(ReadsResources))
        {
            // A picture is read through the ComponentResourceManager local InitializeComponent declares. The target
            // declares one when it has none, and the paste then runs against the document that declares it.
            var (withManager, existingManager) = EnsureComponentResourceManager();
            if (!ReferenceEquals(withManager, this))
                return withManager.PasteControls(clipboard, targetParent, gridStep, occupied, clientSize);
            managerName = existingManager;
        }

        var moved = gridStep is { } offsetBy
            ? OffsetLocations(clipboard, targetParent, offsetBy, occupied ?? [], clientSize)
            : new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        var used = DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        used.Add(ClassName);
        foreach (var method in _class.Members.OfType<MethodBlockSyntax>())
            used.Add(method.SubOrFunctionStatement.Identifier.ValueText);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in clipboard.Controls)
            renamed.Add(control.Name, UniqueControlName(control.Name, used));
        foreach (var auxiliary in clipboard.Auxiliaries)
            renamed.Add(auxiliary.Name, UniqueControlName(auxiliary.Name, used));

        var rewriter = new ClipboardFieldRewriter(renamed);
        var statements = new List<string>();
        foreach (var original in clipboard.Statements)
        {
            var statement = RelocateStatement(original, moved);
            statement = RenameNameProperty(statement, renamed);
            statement = (StatementSyntax)rewriter.Visit(statement)!;
            if (managerName is not null && StatementOwner(statement) is { } owner)
                statement = (StatementSyntax)new ClipboardResourceRewriter(managerName, owner).Visit(statement)!;
            statements.Add(statement.ToString());
        }

        foreach (var control in clipboard.Controls)
        {
            var parent = renamed.GetValueOrDefault(control.ParentName) ?? targetParent;
            // A child of a container's panel keeps its panel when the container is pasted with it: the written Add names the panel.
            var panel = renamed.ContainsKey(control.ParentName) ? control.ParentPanel : null;
            // A child of a TableLayoutPanel keeps its cell when the panel is pasted with it: the written Add names the column and row.
            var cell = renamed.ContainsKey(control.ParentName) ? control.Cell : null;
            var cellArguments = cell is { } at ? $", {at.Column}, {at.Row}" : "";
            statements.Add($"{(parent == "Me" ? "Me" : $"Me.{parent}")}{(panel is null ? "" : "." + panel)}.Controls.Add(Me.{renamed[control.Name]}{cellArguments})");
        }

        var statementPosition = WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);
        var fieldPosition = _class.EndClassStatement.FullSpan.Start;
        var statementText = string.Concat(statements.Select(statement => $"        {statement}{NewLine}"));
        var fieldText = string.Concat(clipboard.Controls.Select(control =>
            $"    Friend WithEvents {renamed[control.Name]} As {control.TypeName}{NewLine}"))
            + string.Concat(clipboard.Auxiliaries.Select(auxiliary =>
                $"    Friend WithEvents {renamed[auxiliary.Name]} As {auxiliary.TypeName}{NewLine}"));
        var source = _text.WithChanges(
            new TextChange(new TextSpan(statementPosition, 0), statementText),
            new TextChange(new TextSpan(fieldPosition, 0), fieldText)).ToString();
        var topLevelNames = clipboard.TopLevelNames.Select(name => renamed[name]).ToArray();
        var allNames = clipboard.Controls.Select(control => renamed[control.Name])
            .Concat(clipboard.Auxiliaries.Select(auxiliary => renamed[auxiliary.Name])).ToArray();
        var resources = clipboard.Resources
            .Select(resource => resource.Entry with
            {
                Name = PastedResourceKey(renamed[resource.Owner], resource.Key),
            })
            .ToArray();
        return new WinFormsControlPasteResult(source, topLevelNames, allNames, resources);
    }

    /// <summary>
    /// The key a copied resource is written under and read from once it is pasted: the new name of the control that reads
    /// it, then the part of the old key after its first dot. "Button1.Image" read by Button1 becomes "Button2.Image" when
    /// Button1 is renamed, and "$this.Font" read by Button1 becomes "Button1.Font". The new key always starts with a name
    /// the paste gave its control, so it cannot take the key of a control the target already has.
    /// </summary>
    internal static string PastedResourceKey(string newOwner, string oldKey)
    {
        var dot = oldKey.IndexOf('.', StringComparison.Ordinal);
        return newOwner + "." + (dot < 0 ? oldKey : oldKey[(dot + 1)..]);
    }

    /// <summary>The copied control a statement assigns or calls on, or null when the statement is not one.</summary>
    private static string? StatementOwner(StatementSyntax statement) => statement switch
    {
        AssignmentStatementSyntax assignment => RootField(assignment.Left),
        ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } => RootField(invocation.Expression),
        _ => null,
    };

    /// <summary>
    /// The resx entries the copied statements read, each with the control that reads it. The entries are the stored values
    /// as they are: nothing is decoded, so a copied picture keeps its exact bytes.
    /// </summary>
    /// <remarks>
    /// <paramref name="carriedEntries"/> are entries no statement reads, such as a read-only property's (ListBox.Items): the
    /// copy carries them under their owner when the owner is copied, so the paste writes them under the new owner's name.
    /// </remarks>
    private static ImmutableArray<WinFormsClipboardResource> CopiedResources(IEnumerable<StatementSyntax> statements,
        WinFormsResourceDocument? resources, IReadOnlyCollection<string> carriedEntries, IReadOnlySet<string> copiedNames)
    {
        if (resources is null) return [];
        var statementReads = statements
            .Select(statement => (Owner: StatementOwner(statement), Keys: ResourceKeysRead(statement)))
            .Where(reads => reads.Owner is not null)
            .SelectMany(reads => reads.Keys.Select(key => (Owner: reads.Owner!, Key: key)));
        var carried = carriedEntries
            .Where(key => key.IndexOf('.', StringComparison.Ordinal) > 0)
            .Select(key => (Owner: key[..key.IndexOf('.', StringComparison.Ordinal)], Key: key))
            .Where(entry => copiedNames.Contains(entry.Owner));
        return statementReads.Concat(carried)
            .Distinct()
            .Select(entry => resources.TryGet(entry.Key) is { } stored
                ? new WinFormsClipboardResource(entry.Owner, entry.Key, stored)
                : null)
            .OfType<WinFormsClipboardResource>()
            .ToImmutableArray();
    }

    private static bool ReadsResources(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Any(invocation => ResourceReadReceiver(invocation) is not null);

    /// <summary>The literal keys of the GetObject and GetString calls in a statement.</summary>
    private static IEnumerable<string> ResourceKeysRead(SyntaxNode statement) =>
        statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Where(invocation => ResourceReadReceiver(invocation) is not null)
            .Select(invocation => FirstArgument(invocation))
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.ValueText);

    private static ExpressionSyntax? FirstArgument(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().FirstOrDefault()?.Expression;

    /// <summary>
    /// The manager a GetObject or GetString call reads through, or null when the invocation is not such a call. The receiver
    /// must be a plain name, which is what InitializeComponent declares its ComponentResourceManager local as.
    /// </summary>
    private static IdentifierNameSyntax? ResourceReadReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member
        && member.Name.Identifier.ValueText is "GetObject" or "GetString"
            ? receiver
            : null;

    private bool IsSelectedOrDescendant(WinFormsDesignerControl control, IReadOnlyCollection<string> selected)
    {
        var current = control;
        while (true)
        {
            if (selected.Contains(current.Name, StringComparer.OrdinalIgnoreCase)) return true;
            if (current.ParentName.Equals("Me", StringComparison.OrdinalIgnoreCase)) return false;
            current = FindControl(current.ParentName);
        }
    }

    private ImmutableArray<StatementSyntax> CaptureInitializationStatements(IReadOnlySet<string> names,
        IReadOnlyCollection<WinFormsLocalizedAssignment>? localizedValues)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        foreach (var statement in _initialize.Statements)
        {
            if (!ReferencedFields(statement).Any(names.Contains)) continue;
            if (statement is AddRemoveHandlerStatementSyntax) continue;

            if (statement is AssignmentStatementSyntax assignment)
            {
                if (RootField(assignment.Left) is { } owner && names.Contains(owner))
                    statements.Add(statement);
                continue;
            }

            if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
            {
                if (IsControlAdd(invocation, names)) continue;
                if (localizedValues is not null && ApplyResourcesOwner(statement) is { } applied && names.Contains(applied))
                {
                    // The control's values leave the .resx as literals, so the call that read them has no place in the fragment.
                    statements.AddRange(LiteralStatements(applied, localizedValues));
                    continue;
                }
                if (RootField(invocation.Expression) is { } owner && names.Contains(owner))
                {
                    statements.Add(statement);
                    continue;
                }
            }

            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedInitializationStatement));
        }

        return statements.ToImmutable();
    }

    /// <summary>The control an ApplyResources statement applies its resources to, or null when the statement is not one.</summary>
    private string? ApplyResourcesOwner(StatementSyntax statement)
    {
        foreach (var (candidate, operation) in ReadResourceOperationStatements())
        {
            if (ReferenceEquals(candidate, statement) && operation.PropertyName is null && operation.ResourceKey is not null)
                return operation.ControlName;
        }
        return null;
    }

    /// <summary>The literal assignments of one control's resolved values, parsed as the statements the fragment carries.</summary>
    private static IEnumerable<StatementSyntax> LiteralStatements(string controlName,
        IReadOnlyCollection<WinFormsLocalizedAssignment> localizedValues) =>
        localizedValues
            .Where(value => value.ControlName.Equals(controlName, StringComparison.OrdinalIgnoreCase))
            .Select(value => SyntaxFactory.ParseExecutableStatement(
                $"Me.{controlName}.{value.PropertyName} = {value.Expression}"));

    private void ValidateClipboardDependencies(WinFormsControlClipboard clipboard)
    {
        foreach (var (field, type) in clipboard.RequiredFields)
        {
            if (!DeclaredNames.Contains(field)
                || !string.Equals(type, FieldType(field), StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerTargetFieldMissing, field));
        }

        var methods = _class.Members.OfType<MethodBlockSyntax>()
            .Select(method => method.SubOrFunctionStatement.Identifier.ValueText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (clipboard.RequiredMethods.Any(method => !methods.Contains(method)))
            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerTargetMethodMissing));
    }

    private string? FieldType(string name) => _class.Members.OfType<FieldDeclarationSyntax>()
        .SelectMany(field => field.Declarators)
        .First(declarator => declarator.Names.Any(identifier =>
            identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase)))
        .AsClause?.ToString();

    private WinFormsClipboardAuxiliary? ColumnAuxiliary(string name)
    {
        if (!DeclaredNames.Contains(name)) return null;
        var creation = _initialize.Statements.OfType<AssignmentStatementSyntax>()
            .FirstOrDefault(statement => ControlName(statement.Left) is { } field
                && field.Equals(name, StringComparison.OrdinalIgnoreCase))?.Right
            as ObjectCreationExpressionSyntax;
        if (creation is null) return null;
        var typeName = creation.Type.ToString().Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        if (!WinFormsCollectionDocument.ColumnTypes.Any(type =>
            typeName.Equals(type, StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("System.Windows.Forms." + type, StringComparison.OrdinalIgnoreCase)))
            return null;
        return new WinFormsClipboardAuxiliary(name, creation.Type.ToString());
    }

    private static IEnumerable<string> ReferencedFields(SyntaxNode statement) =>
        statement.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Expression is MeExpressionSyntax)
            .Select(member => member.Name.Identifier.ValueText)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> ReferencedLocalMethods(SyntaxNode statement)
    {
        var methods = _class.Members.OfType<MethodBlockSyntax>()
            .Select(method => method.SubOrFunctionStatement.Identifier.ValueText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member =>
                    member.Name.Identifier.ValueText,
                _ => null,
            })
            .Where(name => name is not null && methods.Contains(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsControlAdd(InvocationExpressionSyntax invocation, IReadOnlySet<string> names) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax collection,
        } call
        && call.Name.Identifier.ValueText.Equals("Add", StringComparison.OrdinalIgnoreCase)
        && collection.Name.Identifier.ValueText.Equals("Controls", StringComparison.OrdinalIgnoreCase)
        && invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().FirstOrDefault() is { } argument
        && ControlName(argument.Expression) is { } child && names.Contains(child);

    private static string? RootField(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member =>
            member.Name.Identifier.ValueText,
        MemberAccessExpressionSyntax member => RootField(member.Expression),
        _ => null,
    };

    /// <summary>
    /// The new location of each copied top-level control a paste offsets: the ones copied from <paramref name="targetParent"/>
    /// whose copied Location is a literal point. They all move by the same number of steps, so the copies keep their
    /// arrangement. The number is the fewest that puts no copy on an occupied location and keeps every copy inside the container's
    /// client area. When no step count does, the copies wrap to one step from the container's top-left corner, as Visual Studio
    /// keeps a copy visible.
    /// </summary>
    private static Dictionary<string, (int X, int Y)> OffsetLocations(WinFormsControlClipboard clipboard, string targetParent,
        (int Width, int Height) step, IReadOnlyCollection<(int X, int Y)> occupied, (int Width, int Height)? client)
    {
        var origins = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in clipboard.TopLevelNames)
        {
            var control = clipboard.Controls.FirstOrDefault(copied => copied.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (control is null || !ClipboardContainerKey(control).Equals(targetParent, StringComparison.OrdinalIgnoreCase)) continue;
            if (CopiedPair(clipboard.Statements, name, "Location") is not { } at) continue;
            origins[name] = at;
            sizes[name] = CopiedPair(clipboard.Statements, name, "Size") ?? (0, 0);
        }
        if (origins.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

        // Every candidate moves by the same shift. Moving right and down only grows the group, so once it leaves the client
        // area no larger step count fits and the search stops there.
        var occupiedSet = occupied.ToHashSet();
        Dictionary<string, (int X, int Y)> Shift(int dx, int dy) => origins.ToDictionary(pair => pair.Key,
            pair => (pair.Value.X + dx, pair.Value.Y + dy), StringComparer.OrdinalIgnoreCase);
        bool Fits(Dictionary<string, (int X, int Y)> targets) => client is not { } area || targets.All(pair =>
            pair.Value.X >= 0 && pair.Value.Y >= 0
            && pair.Value.X + sizes[pair.Key].Width <= area.Width && pair.Value.Y + sizes[pair.Key].Height <= area.Height);
        bool Free(Dictionary<string, (int X, int Y)> targets) => targets.Values.All(at => !occupiedSet.Contains(at));

        for (var steps = 1; ; steps++)
        {
            var targets = Shift(steps * step.Width, steps * step.Height);
            if (!Fits(targets)) break;
            if (Free(targets)) return targets;
        }

        // No step count keeps the copies inside and free, so they wrap: the group's top-left corner lands one step from the
        // container's top-left, and the copies are visible even if they cover a control there.
        var minX = origins.Values.Min(at => at.X);
        var minY = origins.Values.Min(at => at.Y);
        return Shift(step.Width - minX, step.Height - minY);
    }

    /// <summary>The container a copied control sat in, named as <see cref="ResolveParentKey"/> names it: its owner, or owner.panel.</summary>
    private static string ClipboardContainerKey(WinFormsClipboardControl control) =>
        control.ParentPanel is null ? control.ParentName : control.ParentName + "." + control.ParentPanel;

    /// <summary>
    /// The literal pair of integers the copied statements assign to a control's <paramref name="property"/> (a Location or a
    /// Size), the last assignment that takes effect, or null when there is none or it is not a literal pair.
    /// </summary>
    private static (int X, int Y)? CopiedPair(IEnumerable<StatementSyntax> statements, string name, string property)
    {
        (int X, int Y)? value = null;
        foreach (var assignment in statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax left
                || !left.Name.Identifier.ValueText.Equals(property, StringComparison.OrdinalIgnoreCase)
                || RootField(left.Expression) is not { } owner || !owner.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (assignment.Right is ObjectCreationExpressionSyntax
                {
                    ArgumentList.Arguments: [SimpleArgumentSyntax { Expression: LiteralExpressionSyntax { Token.Value: int x } },
                        SimpleArgumentSyntax { Expression: LiteralExpressionSyntax { Token.Value: int y } }],
                })
                value = (x, y);
            else
                value = null;
        }
        return value;
    }

    /// <summary>
    /// Writes a copied Location assignment at the control's offset location. Other statements, and a control that is not
    /// offset, come back unchanged.
    /// </summary>
    private static StatementSyntax RelocateStatement(StatementSyntax statement, IReadOnlyDictionary<string, (int X, int Y)> moved)
    {
        if (moved.Count == 0
            || statement is not AssignmentStatementSyntax
            {
                Left: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Location" } left,
            } assignment
            || RootField(left.Expression) is not { } owner
            || !moved.TryGetValue(owner, out var at)) return statement;
        return assignment.WithRight(SyntaxFactory.ParseExpression($"New System.Drawing.Point({at.X}, {at.Y})"));
    }

    private static StatementSyntax RenameNameProperty(StatementSyntax statement,
        IReadOnlyDictionary<string, string> renamed)
    {
        if (statement is not AssignmentStatementSyntax
            { Left: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Name" } left } assignment
            || RootField(left.Expression) is not { } owner
            || !renamed.TryGetValue(owner, out var newName)) return statement;
        return assignment.WithRight(SyntaxFactory.ParseExpression($"\"{newName}\""));
    }

    private static string UniqueControlName(string oldName, ISet<string> used)
    {
        if (SyntaxFacts.IsValidIdentifier(oldName) && used.Add(oldName)) return oldName;
        var stem = oldName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (stem.Length == 0 || !SyntaxFacts.IsValidIdentifier(stem + "1")) stem = "Control";
        for (var number = 1; ; number++)
        {
            var candidate = stem + number;
            if (used.Add(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Points a copied GetObject or GetString read at the target's ComponentResourceManager local and at the key the pasted
    /// control reads from (see <see cref="PastedResourceKey"/>). The read is rebuilt rather than patched node by node, so
    /// the rest of the statement keeps its text.
    /// </summary>
    private sealed class ClipboardResourceRewriter(string managerName, string newOwner) : VisualBasicSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (ResourceReadReceiver(visited) is not { } receiver || visited.Expression is not MemberAccessExpressionSyntax member)
                return visited;
            var manager = SyntaxFactory.IdentifierName(managerName).WithTriviaFrom(receiver);
            var rebuilt = visited.WithExpression(member.WithExpression(manager));
            if (visited.ArgumentList is not { } argumentList
                || argumentList.Arguments.FirstOrDefault() is not SimpleArgumentSyntax { Expression: LiteralExpressionSyntax key } firstArgument)
                return rebuilt;

            var newKey = SyntaxFactory.StringLiteralExpression(
                SyntaxFactory.Literal(PastedResourceKey(newOwner, key.Token.ValueText))).WithTriviaFrom(key);
            ArgumentSyntax replacement = firstArgument.WithExpression(newKey);
            ArgumentSyntax original = firstArgument;
            var arguments = argumentList.Arguments.Replace(original, replacement);
            return rebuilt.WithArgumentList(argumentList.WithArguments(arguments));
        }
    }

    private sealed class ClipboardFieldRewriter(IReadOnlyDictionary<string, string> renamed)
        : VisualBasicSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            if (visited.Expression is not MeExpressionSyntax
                || !renamed.TryGetValue(visited.Name.Identifier.ValueText, out var name))
                return visited;
            return visited.WithName(SyntaxFactory.IdentifierName(name).WithTriviaFrom(visited.Name));
        }
    }
}
