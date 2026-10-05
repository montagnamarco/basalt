using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsMenuDocument
{
    public IReadOnlyDictionary<string, ExpressionSyntax> ReadItemProperties(string strip, string itemName)
    {
        RequireItem(strip, itemName);
        return ItemAssignments(itemName).GroupBy(assignment =>
                ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.ValueText,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Right, StringComparer.OrdinalIgnoreCase);
    }

    public string SetItemProperty(string strip, string itemName, string property, string? expression)
    {
        RequireItem(strip, itemName);
        if (!SyntaxFacts.IsValidIdentifier(property))
        {
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuPropertyMustBeIdentifier), nameof(property));
        }

        var assignments = ItemAssignments(itemName).Where(assignment =>
            ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.ValueText
                .Equals(property, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (expression is null)
        {
            return _source.WithChanges(assignments.Select(assignment =>
                new TextChange(assignment.Span, ""))).ToString();
        }

        if (SyntaxFactory.ParseExpression(expression).ContainsDiagnostics)
        {
            throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerInvalidExpression));
        }

        if (assignments.LastOrDefault() is { } last)
        {
            return _source.WithChanges(new TextChange(last.Right.Span, expression)).ToString();
        }

        var newline = _source.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var statement = $"        Me.{itemName}.{property} = {expression}{newline}";
        return _source.WithChanges(new TextChange(
            new TextSpan(_initialize.EndSubOrFunctionStatement.FullSpan.Start, 0), statement)).ToString();
    }

    private IEnumerable<AssignmentStatementSyntax> ItemAssignments(string itemName) =>
        _initialize.Statements.OfType<AssignmentStatementSyntax>().Where(assignment =>
            assignment.Left is MemberAccessExpressionSyntax member &&
            Name(member.Expression)?.Equals(itemName, StringComparison.OrdinalIgnoreCase) == true);

    private void RequireItem(string strip, string itemName)
    {
        if (!Read(strip).Any(item => item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsMenuItemNotInMenu), nameof(itemName));
        }
    }
}
