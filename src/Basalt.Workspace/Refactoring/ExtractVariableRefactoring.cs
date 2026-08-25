using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Giving a name to an expression.
///
/// Roslyn does not offer this one publicly, so it is done here: the expression
/// is replaced by a name and a Dim is written above the statement it was in.
/// Deliberately limited to a single expression inside one statement, which is
/// what people reach for it to do.
/// </summary>
public sealed class ExtractVariableRefactoring
{
    private readonly Solution _solution;

    public ExtractVariableRefactoring(Solution solution) => _solution = solution;

    /// <summary>
    /// Names an expression, as a variable or as a constant.
    ///
    /// The two differ in one word of the declaration and in what they will
    /// accept: a constant has to be worked out at compile time, so an
    /// expression mentioning a variable is refused rather than written as a
    /// Const that will not compile.
    /// </summary>
    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int start, int length, string name,
        bool asConstant = false, CancellationToken ct = default)
    {
        var title = asConstant ? "Extract Constant" : "Extract Variable";

        if (!RenameRefactoring.IsValidName(name))
            return RefactoringPreview.Refused(title, $"'{name}' is not a valid name.");

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                title, "Extracting a variable is only supported in Visual Basic so far.");
        }

        if (length <= 0)
            return RefactoringPreview.Refused(title, "Select the expression to extract.");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null) return RefactoringPreview.Refused(title, "The file could not be read.");

        var span = new TextSpan(start, length);

        if (FindExpression(root, span) is not { } expression)
        {
            return RefactoringPreview.Refused(
                title, "That selection is not a single expression.");
        }

        if (expression.FirstAncestorOrSelf<StatementSyntax>() is not { } statement)
        {
            return RefactoringPreview.Refused(
                title, "That expression is not inside a statement.");
        }

        if (asConstant && NonConstantPart(expression) is { } moving)
        {
            return RefactoringPreview.Refused(
                title,
                $"A constant has to be known at compile time, and '{moving}' is not.");
        }

        var updated = Rewrite(text, expression, statement, name, asConstant);

        var what = asConstant ? "constant" : "variable";

        return new RefactoringPreview(
            $"Extract '{Shorten(expression.ToString())}' to {what} '{name}'",
        [
            new FileChangePreview(filePath, text.ToString(), updated)
        ]);
    }

    /// <summary>
    /// The expression the selection covers, or null when it covers something
    /// else.
    ///
    /// The selection has to be the expression exactly: a partial one would
    /// produce code that does not compile, and half an expression is not
    /// something anyone means to name.
    /// </summary>
    private static ExpressionSyntax? FindExpression(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);

        // Walk out to the widest expression that the selection still covers.
        ExpressionSyntax? found = null;

        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is not ExpressionSyntax expression) break;

            if (expression.Span != span && !span.Contains(expression.Span)) break;

            found = expression;
        }

        return found?.Span == span ? found : null;
    }

    /// <summary>
    /// Writes the new text: a Dim above, the name in place of the expression.
    ///
    /// Done on the text rather than the tree so the rest of the file keeps its
    /// formatting exactly: a tree rewrite reformats lines nobody asked about.
    /// </summary>
    /// <summary>
    /// The first part of an expression that is not a literal, or null when
    /// every part is one.
    ///
    /// Only literals and the operators between them can be a constant. A name
    /// might be another constant, but proving that needs the semantic model,
    /// and refusing is the safer answer: the user can extract a variable.
    /// </summary>
    private static string? NonConstantPart(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax => null,

        ParenthesizedExpressionSyntax parenthesised =>
            NonConstantPart(parenthesised.Expression),

        UnaryExpressionSyntax unary => NonConstantPart(unary.Operand),

        BinaryExpressionSyntax binary =>
            NonConstantPart(binary.Left) ?? NonConstantPart(binary.Right),

        _ => expression.ToString()
    };

    private static string Rewrite(
        SourceText text, ExpressionSyntax expression, StatementSyntax statement,
        string name, bool asConstant)
    {
        var statementLine = text.Lines.GetLineFromPosition(statement.SpanStart);

        var indent = new string(' ', statement.SpanStart - statementLine.Start);

        var lineEnding = text.ToString().Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var keyword = asConstant ? "Const" : "Dim";

        var declaration = $"{indent}{keyword} {name} = {expression}{lineEnding}";

        // The expression is replaced first: inserting the declaration above
        // would move it otherwise.
        var replaced = text.Replace(expression.Span, name);

        return replaced.Replace(new TextSpan(statementLine.Start, 0), declaration).ToString();
    }

    /// <summary>An expression as a title can show it.</summary>
    private static string Shorten(string expression)
    {
        var single = string.Join(" ", expression.Split(
            ['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return single.Length <= 40 ? single : single[..37] + "…";
    }
}
