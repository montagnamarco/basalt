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
        ImmutableHashSet<string> requiredMethods)
    {
        Controls = controls;
        Auxiliaries = auxiliaries;
        Statements = statements;
        TopLevelNames = topLevelNames;
        RequiredFields = requiredFields;
        RequiredMethods = requiredMethods;
    }

    internal ImmutableArray<WinFormsClipboardControl> Controls { get; }
    internal ImmutableArray<WinFormsClipboardAuxiliary> Auxiliaries { get; }
    internal ImmutableArray<StatementSyntax> Statements { get; }
    internal ImmutableDictionary<string, string?> RequiredFields { get; }
    internal ImmutableHashSet<string> RequiredMethods { get; }

    public IReadOnlyList<string> TopLevelNames { get; }
}

internal sealed record WinFormsClipboardControl(string Name, string TypeName, string ParentName);
internal sealed record WinFormsClipboardAuxiliary(string Name, string TypeName);

public sealed record WinFormsControlPasteResult(string Source,
    IReadOnlyList<string> TopLevelNames, IReadOnlyList<string> AllNames);

public sealed partial class WinFormsDesignerDocument
{
    public WinFormsControlClipboard CopyControls(IEnumerable<string> selectedNames)
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
        var controlStatements = CaptureInitializationStatements(names);
        var auxiliaries = controlStatements.SelectMany(ReferencedFields)
            .Where(name => !names.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ColumnAuxiliary(name))
            .OfType<WinFormsClipboardAuxiliary>()
            .ToImmutableArray();
        foreach (var auxiliary in auxiliaries) names.Add(auxiliary.Name);
        var statements = CaptureInitializationStatements(names);
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
                control.Name, control.TypeName, control.ParentName)).ToImmutableArray(),
            auxiliaries,
            statements, topLevel, requiredFields.ToImmutable(), requiredMethods.ToImmutable());
    }

    public WinFormsControlPasteResult PasteControls(WinFormsControlClipboard clipboard, string targetParent)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        targetParent = FindControl(targetParent).Name;
        ValidateClipboardDependencies(clipboard);

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
            statements.Add(statement.ToString());
        }

        foreach (var control in clipboard.Controls)
        {
            var parent = renamed.GetValueOrDefault(control.ParentName) ?? targetParent;
            statements.Add($"{(parent == "Me" ? "Me" : $"Me.{parent}")}.Controls.Add(Me.{renamed[control.Name]})");
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
        return new WinFormsControlPasteResult(source, topLevelNames, allNames);
    }

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

    private ImmutableArray<StatementSyntax> CaptureInitializationStatements(IReadOnlySet<string> names)
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
