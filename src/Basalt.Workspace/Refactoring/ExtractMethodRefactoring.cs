using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Lifting a run of statements into a method of their own.
///
/// The signature is not guessed: Roslyn's data flow analysis says which
/// locals the selection reads from outside (they become parameters) and which
/// it writes that the rest of the method still needs (that becomes the return
/// value). A selection that needs to hand back more than one value is refused
/// rather than turned into ByRef parameters behind the user's back.
/// </summary>
public sealed class ExtractMethodRefactoring
{
    private const string Title = "Extract Method";

    private readonly Solution _solution;

    public ExtractMethodRefactoring(Solution solution) => _solution = solution;

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int start, int length, string name, CancellationToken ct = default)
    {
        if (!RenameRefactoring.IsValidName(name))
            return RefactoringPreview.Refused(Title, $"'{name}' is not a valid name.");

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(Title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                Title, "Extracting a method is only supported in Visual Basic so far.");
        }

        if (length <= 0)
            return RefactoringPreview.Refused(Title, "Select the statements to extract.");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null || model is null)
            return RefactoringPreview.Refused(Title, "The file could not be read.");

        var span = new TextSpan(start, length);
        var statements = StatementsIn(root, span);

        if (statements.Count == 0)
        {
            return RefactoringPreview.Refused(
                Title, "That selection is not a whole statement.");
        }

        if (statements[0].FirstAncestorOrSelf<MethodBlockBaseSyntax>() is not { } source)
        {
            return RefactoringPreview.Refused(
                Title, "Those statements are not inside a method.");
        }

        // A Return inside the selection would return from the new method, not
        // from the one it was lifted out of: a change in meaning the user is
        // told about rather than handed silently.
        var control = model.AnalyzeControlFlow(statements[0], statements[^1]);

        if (control is null || !control.Succeeded)
            return RefactoringPreview.Refused(Title, "That selection could not be analysed.");

        if (control.ReturnStatements.Length > 0)
        {
            return RefactoringPreview.Refused(
                Title, "That selection returns from the method, so it cannot be lifted out of it.");
        }

        var flow = model.AnalyzeDataFlow(statements[0], statements[^1]);

        if (flow is null || !flow.Succeeded)
            return RefactoringPreview.Refused(Title, "That selection could not be analysed.");

        // Written inside and still read afterwards: the value has to come back.
        var returned = flow.DataFlowsOut
            .OfType<ILocalSymbol>()
            .ToList();

        if (returned.Count > 1)
        {
            var names = string.Join(", ", returned.Select(r => $"'{r.Name}'"));

            return RefactoringPreview.Refused(
                Title, $"That selection would have to return {names}, which is more than one value.");
        }

        // Read inside but coming from outside: they become parameters.
        // DataFlowsIn covers the enclosing method's own parameters too, since
        // one read inside the selection flows in like any other variable.
        var parameters = flow.DataFlowsIn
            .Where(s => s is ILocalSymbol or IParameterSymbol)
            .Where(s => !flow.VariablesDeclared.Contains(s, SymbolEqualityComparer.Default))
            .ToList();

        var updated = Rewrite(text, statements, source, name, parameters, returned.FirstOrDefault());

        var signature = returned.Count == 1 ? $"Function {name}" : $"Sub {name}";

        return new RefactoringPreview(
            $"Extract {statements.Count} statement{(statements.Count == 1 ? "" : "s")} into {signature}",
            [new FileChangePreview(filePath, text.ToString(), updated)]);
    }

    /// <summary>
    /// The statements the selection covers, in order.
    ///
    /// Empty when the selection cuts a statement in half: extracting part of
    /// one would produce code that does not compile.
    /// </summary>
    private static List<StatementSyntax> StatementsIn(SyntaxNode root, TextSpan span)
    {
        var all = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is MethodBlockBaseSyntax or MultiLineIfBlockSyntax or ElseBlockSyntax
                     || s.Parent is DoLoopBlockSyntax or ForBlockSyntax or ForEachBlockSyntax
                     || s.Parent is WhileBlockSyntax or SelectBlockSyntax or CaseBlockSyntax)
            .Where(s => span.Contains(s.Span))
            .ToList();

        // Keep only the outermost: a For block and the statements inside it
        // would otherwise both be listed, and the body would be extracted twice.
        var outermost = all
            .Where(s => !all.Any(other => other != s && other.Span.Contains(s.Span)))
            .OrderBy(s => s.SpanStart)
            .ToList();

        if (outermost.Count == 0) return [];

        // The selection has to cover them fully and start at the first one:
        // trailing text that is not a statement means a partial selection.
        var covered = TextSpan.FromBounds(outermost[0].SpanStart, outermost[^1].Span.End);

        return span.Contains(covered) ? outermost : [];
    }

    /// <summary>
    /// Writes the call in place of the statements, and the method after the
    /// one they came from.
    ///
    /// Text rather than tree, as with extracting a variable: the rest of the
    /// file keeps the formatting it had.
    /// </summary>
    private static string Rewrite(
        SourceText text,
        List<StatementSyntax> statements,
        MethodBlockBaseSyntax source,
        string name,
        List<ISymbol> parameters,
        ILocalSymbol? returned)
    {
        var lineEnding = text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var firstLine = text.Lines.GetLineFromPosition(statements[0].SpanStart);
        var indent = new string(' ', statements[0].SpanStart - firstLine.Start);
        var methodIndent = new string(' ', IndentOf(text, source.SpanStart));
        var bodyIndent = methodIndent + "    ";

        var arguments = string.Join(", ", parameters.Select(p => p.Name));
        var call = returned is null
            ? $"{indent}{name}({arguments})"
            : $"{indent}{returned.Name} = {name}({arguments})";

        // A local that is declared inside the selection and returned needs its
        // Dim at the call site, since the declaration moved into the method.
        if (returned is not null && statements.Any(s => Declares(s, returned)))
            call = $"{indent}Dim {returned.Name} = {name}({arguments})";

        var parameterList = string.Join(", ", parameters.Select(
            p => $"{p.Name} As {TypeNameOf(p)}"));

        var body = string.Join(lineEnding, statements.Select(
            s => bodyIndent + Reindent(text, s, bodyIndent, lineEnding)));

        var method = returned is null
            ? $"{lineEnding}{methodIndent}Private Sub {name}({parameterList}){lineEnding}"
              + $"{body}{lineEnding}{methodIndent}End Sub{lineEnding}"
            : $"{lineEnding}{methodIndent}Private Function {name}({parameterList}) "
              + $"As {returned.Type.ToDisplayString()}{lineEnding}"
              + $"{body}{lineEnding}{bodyIndent}Return {returned.Name}{lineEnding}"
              + $"{methodIndent}End Function{lineEnding}";

        var extracted = TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);

        // The method is inserted after the statements are replaced, so its
        // position is worked out against the text as it will then be.
        var afterSource = source.Span.End;
        var callDelta = call.Length - extracted.Length;

        var replaced = text.Replace(extracted, call);

        return replaced
            .Replace(new TextSpan(afterSource + callDelta, 0), method)
            .ToString();
    }

    /// <summary>Whether a statement declares the given local.</summary>
    private static bool Declares(StatementSyntax statement, ILocalSymbol local) =>
        statement is LocalDeclarationStatementSyntax declaration
        && declaration.Declarators
            .SelectMany(d => d.Names)
            .Any(n => string.Equals(n.Identifier.ValueText, local.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How far a line is indented.</summary>
    private static int IndentOf(SourceText text, int position) =>
        position - text.Lines.GetLineFromPosition(position).Start;

    /// <summary>
    /// A statement's text with its inner lines lined up under the new indent.
    ///
    /// The first line is left to the caller, which already wrote the indent.
    /// </summary>
    private static string Reindent(
        SourceText text, StatementSyntax statement, string bodyIndent, string lineEnding)
    {
        var lines = statement.ToString().Replace("\r\n", "\n").Split('\n');

        if (lines.Length == 1) return lines[0];

        var original = new string(' ', IndentOf(text, statement.SpanStart));

        var rest = lines.Skip(1).Select(line =>
            line.StartsWith(original, StringComparison.Ordinal)
                ? bodyIndent + line[original.Length..]
                : line);

        return string.Join(lineEnding, new[] { lines[0] }.Concat(rest));
    }

    /// <summary>The declared type of a parameter or local, as source writes it.</summary>
    private static string TypeNameOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type.ToDisplayString(),
        IParameterSymbol parameter => parameter.Type.ToDisplayString(),
        _ => "Object"
    };
}
