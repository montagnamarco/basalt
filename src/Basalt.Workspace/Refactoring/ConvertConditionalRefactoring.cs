using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Turning an If chain into a Select Case, and back.
///
/// Only where the two really mean the same thing: a Select Case tests one
/// value against several, so an If chain qualifies only when every branch
/// compares the same expression for equality. A chain that tests different
/// things is refused rather than rewritten into something that behaves
/// differently.
/// </summary>
public sealed class ConvertConditionalRefactoring
{
    private readonly Solution _solution;

    public ConvertConditionalRefactoring(Solution solution) => _solution = solution;

    /// <summary>Converts whichever of the two the caret is in.</summary>
    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        const string title = "Convert If and Select Case";

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                title, "This conversion is only supported in Visual Basic so far.");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null) return RefactoringPreview.Refused(title, "The file could not be read.");

        var node = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1))).Parent;

        if (node?.FirstAncestorOrSelf<MultiLineIfBlockSyntax>() is { } ifBlock)
            return ToSelectCase(filePath, text, ifBlock);

        if (node?.FirstAncestorOrSelf<SelectBlockSyntax>() is { } selectBlock)
            return ToIf(filePath, text, selectBlock);

        return RefactoringPreview.Refused(
            title, "Put the caret in an If or a Select Case to convert it.");
    }

    // If -> Select Case

    private static RefactoringPreview ToSelectCase(
        string filePath, SourceText text, MultiLineIfBlockSyntax ifBlock)
    {
        const string title = "Convert If to Select Case";

        // Every branch has to compare the same thing, or the two forms do not
        // mean the same. What that thing is comes from the first branch.
        if (Compared(ifBlock.IfStatement.Condition) is not { } subject)
        {
            return RefactoringPreview.Refused(
                title, "This If does not compare one value for equality.");
        }

        var branches = new List<(ExpressionSyntax Value, IReadOnlyList<StatementSyntax> Body)>();

        if (ComparedValue(ifBlock.IfStatement.Condition) is not { } firstValue)
            return RefactoringPreview.Refused(title, "This If does not test a single value.");

        branches.Add((firstValue, ifBlock.Statements));

        foreach (var branch in ifBlock.ElseIfBlocks)
        {
            if (Compared(branch.ElseIfStatement.Condition) is not { } other
                || !SameSubject(subject, other))
            {
                return RefactoringPreview.Refused(
                    title,
                    $"Not every branch tests '{subject}', so a Select Case would "
                  + "not mean the same thing.");
            }

            if (ComparedValue(branch.ElseIfStatement.Condition) is not { } value)
                return RefactoringPreview.Refused(title, "A branch does not test a single value.");

            branches.Add((value, branch.Statements));
        }

        if (branches.Count < 2)
        {
            return RefactoringPreview.Refused(
                title, "A Select Case is only worth it from two branches upwards.");
        }

        var lineEnding = LineEndingOf(text);
        var indent = IndentOf(text, ifBlock.SpanStart);

        var written = new System.Text.StringBuilder();

        written.Append(indent).Append("Select Case ").Append(subject).Append(lineEnding);

        foreach (var (value, body) in branches)
        {
            written.Append(indent).Append("    Case ").Append(value).Append(lineEnding);
            AppendBody(written, text, body, indent + "        ", lineEnding);
        }

        if (ifBlock.ElseBlock is { } otherwise)
        {
            written.Append(indent).Append("    Case Else").Append(lineEnding);
            AppendBody(written, text, otherwise.Statements, indent + "        ", lineEnding);
        }

        written.Append(indent).Append("End Select");

        return new RefactoringPreview(
            $"Convert If to Select Case on '{subject}'",
            [new FileChangePreview(filePath, text.ToString(),
                text.Replace(ifBlock.Span, written.ToString()).ToString())]);
    }

    // Select Case -> If

    private static RefactoringPreview ToIf(
        string filePath, SourceText text, SelectBlockSyntax selectBlock)
    {
        const string title = "Convert Select Case to If";

        var subject = selectBlock.SelectStatement.Expression;

        var cases = selectBlock.CaseBlocks
            .Where(c => !IsCaseElse(c))
            .ToList();

        if (cases.Count == 0)
            return RefactoringPreview.Refused(title, "This Select Case has no cases.");

        // Only the simple form: Case 1, Case 2. A range or a comparison would
        // need more than an equality test to say the same thing.
        foreach (var block in cases)
        {
            if (block.CaseStatement.Cases.Any(c => c is not SimpleCaseClauseSyntax))
            {
                return RefactoringPreview.Refused(
                    title,
                    "A case tests a range or a comparison, which an If chain "
                  + "would not say the same way.");
            }
        }

        var lineEnding = LineEndingOf(text);
        var indent = IndentOf(text, selectBlock.SpanStart);

        var written = new System.Text.StringBuilder();

        for (var i = 0; i < cases.Count; i++)
        {
            var values = cases[i].CaseStatement.Cases
                .OfType<SimpleCaseClauseSyntax>()
                .Select(c => $"{subject} = {c.Value}");

            // Case 1, 2 becomes an Or of the two comparisons.
            var condition = string.Join(" Or ", values);

            written
                .Append(indent)
                .Append(i == 0 ? "If " : "ElseIf ")
                .Append(condition)
                .Append(" Then")
                .Append(lineEnding);

            AppendBody(written, text, cases[i].Statements, indent + "    ", lineEnding);
        }

        var otherwise = selectBlock.CaseBlocks
            .FirstOrDefault(IsCaseElse);

        if (otherwise is not null)
        {
            written.Append(indent).Append("Else").Append(lineEnding);
            AppendBody(written, text, otherwise.Statements, indent + "    ", lineEnding);
        }

        written.Append(indent).Append("End If");

        return new RefactoringPreview(
            $"Convert Select Case on '{subject}' to If",
            [new FileChangePreview(filePath, text.ToString(),
                text.Replace(selectBlock.Span, written.ToString()).ToString())]);
    }

    // What the two forms have in common

    /// <summary>
    /// Whether a block is the Case Else.
    ///
    /// Told by its clause rather than by the statement type: Roslyn gives
    /// every case the same CaseStatementSyntax, and only the clause inside
    /// says which kind it is.
    /// </summary>
    private static bool IsCaseElse(CaseBlockSyntax block) =>
        block.CaseStatement.Cases.Any(c => c is ElseCaseClauseSyntax);

    /// <summary>The thing a condition compares, when it compares one for equality.</summary>
    private static ExpressionSyntax? Compared(ExpressionSyntax condition) =>
        condition is BinaryExpressionSyntax
        {
            OperatorToken.RawKind: (int)SyntaxKind.EqualsToken
        } equality
            ? equality.Left
            : null;

    /// <summary>What it is compared against.</summary>
    private static ExpressionSyntax? ComparedValue(ExpressionSyntax condition) =>
        condition is BinaryExpressionSyntax
        {
            OperatorToken.RawKind: (int)SyntaxKind.EqualsToken
        } equality
            ? equality.Right
            : null;

    /// <summary>Whether two branches test the same thing, as written.</summary>
    private static bool SameSubject(ExpressionSyntax first, ExpressionSyntax second) =>
        string.Equals(first.ToString(), second.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes a branch's statements under a new indent.</summary>
    private static void AppendBody(
        System.Text.StringBuilder written,
        SourceText text,
        IReadOnlyList<StatementSyntax> body,
        string indent,
        string lineEnding)
    {
        foreach (var statement in body)
        {
            var original = IndentOf(text, statement.SpanStart);

            var lines = statement.ToString().Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = i == 0
                    ? lines[i]
                    : lines[i].StartsWith(original, StringComparison.Ordinal)
                        ? lines[i][original.Length..]
                        : lines[i].TrimStart();

                written.Append(indent).Append(line).Append(lineEnding);
            }
        }
    }

    private static string IndentOf(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);

        return new string(' ', position - line.Start);
    }

    private static string LineEndingOf(SourceText text) =>
        text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
