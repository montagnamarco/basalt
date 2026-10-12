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

    public WinFormsControlPasteResult PasteControls(WinFormsControlClipboard clipboard, string targetParent)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        targetParent = ResolveParentKey(targetParent);
        ValidateClipboardDependencies(clipboard);

        string? managerName = null;
        if (clipboard.Statements.Any(ReadsResources))
        {
            // A picture is read through the ComponentResourceManager local InitializeComponent declares. The target
            // declares one when it has none, and the paste then runs against the document that declares it.
            var (withManager, existingManager) = EnsureComponentResourceManager();
            if (!ReferenceEquals(withManager, this)) return withManager.PasteControls(clipboard, targetParent);
            managerName = existingManager;
        }

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
            var statement = RenameNameProperty(original, renamed);
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
