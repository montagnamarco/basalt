using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>What a method's parameters should become.</summary>
/// <param name="Order">
/// Where each parameter comes from, by its index in the original list. A
/// parameter left out of this is removed; an index appearing later than
/// another is a reorder.
/// </param>
/// <param name="Added">Parameters that did not exist, with the argument to pass.</param>
public sealed record SignatureChange(
    IReadOnlyList<int> Order,
    IReadOnlyList<(string Name, string Type, string DefaultArgument)> Added);

/// <summary>
/// Changing a method's parameters, and every call to it.
///
/// The declaration alone is the easy half: a signature changed without its
/// calls leaves code that does not compile, so the calls are what this is
/// really about. A call it cannot rewrite with certainty stops the whole
/// change rather than leaving some updated and some not.
/// </summary>
public sealed class ChangeSignatureRefactoring
{
    private const string Title = "Change Signature";

    private readonly Solution _solution;

    public ChangeSignatureRefactoring(Solution solution) => _solution = solution;

    /// <summary>The parameters a method has now, for the dialog to show.</summary>
    public async Task<IReadOnlyList<(string Name, string Type)>> ParametersAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        var found = await FindMethodAsync(filePath, position, ct).ConfigureAwait(false);

        if (found.Method is not { } method) return [];

        return
        [
            .. method.BlockStatement.ParameterList?.Parameters.Select(p =>
                (p.Identifier.Identifier.ValueText,
                 // A property here, unlike on a declarator's clause, where
                 // the same name is a method.
                 (p.AsClause as SimpleAsClauseSyntax)?.Type.ToString() ?? "Object")) ?? []
        ];
    }

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int position, SignatureChange change, CancellationToken ct = default)
    {
        var found = await FindMethodAsync(filePath, position, ct).ConfigureAwait(false);

        if (found.Refusal is { } refusal) return refusal;

        var (document, method, text) = (found.Document!, found.Method!, found.Text!);

        var parameters = method.BlockStatement.ParameterList?.Parameters.ToList() ?? [];

        if (parameters.Count == 0 && change.Added.Count == 0)
            return RefactoringPreview.Refused(Title, "This method has no parameters.");

        if (change.Order.Any(i => i < 0 || i >= parameters.Count))
            return RefactoringPreview.Refused(Title, "That is not one of the parameters.");

        if (change.Order.Count == parameters.Count
            && change.Added.Count == 0
            && change.Order.SequenceEqual(Enumerable.Range(0, parameters.Count)))
        {
            return RefactoringPreview.Refused(Title, "Nothing about the signature changed.");
        }

        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        // RS1039 claims this returns null for every spelling of the call —
        // the block, its statement, and the precisely typed statement. All
        // three were tried against a real compilation and all three return
        // the method symbol, so the rule is wrong for Visual Basic here.
        // Suppressed rather than worked around, because the alternatives
        // were contortions that did not silence it either.
#pragma warning disable RS1039
        if (method.BlockStatement is not MethodStatementSyntax declaration
            || model?.GetDeclaredSymbol(declaration, ct) is not { } symbol)
        {
            return RefactoringPreview.Refused(Title, "That method could not be resolved.");
        }
#pragma warning restore RS1039

        var name = symbol.Name;

        // The declaration first, then every call, each as a text edit on the
        // file it is in.
        var edits = new Dictionary<string, List<(TextSpan Span, string Text)>>(
            StringComparer.Ordinal);

        void Edit(string path, TextSpan span, string replacement)
        {
            if (!edits.TryGetValue(path, out var list)) edits[path] = list = [];

            list.Add((span, replacement));
        }

        var written = string.Join(", ",
            change.Order.Select(i => parameters[i].ToString())
                .Concat(change.Added.Select(a => $"{a.Name} As {a.Type}")));

        if (method.BlockStatement.ParameterList is { } list)
        {
            Edit(filePath, list.Span, $"({written})");
        }
        else
        {
            // A method with no parameter list at all: one is added after the
            // name, which is where it would have been written.
            Edit(filePath, new TextSpan(method.BlockStatement.Span.End, 0), $"({written})");
        }

        var callSites = await SymbolFinder
            .FindReferencesAsync(symbol, _solution, ct)
            .ConfigureAwait(false);

        foreach (var location in callSites.SelectMany(r => r.Locations))
        {
            if (location.IsImplicit) continue;

            var callPath = location.Document.FilePath;

            if (callPath is null) continue;

            var callRoot = await location.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);

            if (callRoot is null) continue;

            var node = callRoot.FindNode(location.Location.SourceSpan);

            // The call the name belongs to. A name that is not being called —
            // an AddressOf, say — has no argument list to rewrite.
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocation?.ArgumentList is not { } arguments)
            {
                // A call written without brackets is still a call: "M 1, 2".
                if (node.FirstAncestorOrSelf<CallStatementSyntax>() is not null)
                {
                    return RefactoringPreview.Refused(
                        Title,
                        $"'{name}' is called without brackets somewhere, which this "
                      + "cannot rewrite with certainty.");
                }

                continue;
            }

            if (arguments.Arguments.Any(a => a.IsNamed))
            {
                return RefactoringPreview.Refused(
                    Title,
                    $"'{name}' is called with named arguments somewhere, so reordering "
                  + "would not mean the same thing.");
            }

            var given = arguments.Arguments.ToList();

            var rewritten = string.Join(", ",
                change.Order
                    .Select(i => i < given.Count ? given[i].ToString() : "Nothing")
                    .Concat(change.Added.Select(a => a.DefaultArgument)));

            Edit(callPath, arguments.Span, $"({rewritten})");
        }

        var changes = new List<FileChangePreview>();

        foreach (var (path, spans) in edits)
        {
            var original = string.Equals(path, filePath, StringComparison.Ordinal)
                ? text
                : await TextOfAsync(path, ct).ConfigureAwait(false);

            if (original is null) continue;

            var updated = original;

            // Later spans first, so an edit does not move the next one.
            foreach (var (span, replacement) in spans.OrderByDescending(s => s.Span.Start))
                updated = updated.Replace(span, replacement);

            changes.Add(new FileChangePreview(path, original.ToString(), updated.ToString()));
        }

        var calls = changes.Count - 1;

        return new RefactoringPreview(
            $"Change the signature of '{name}'"
          + (calls > 0 ? $" and {calls} file{(calls == 1 ? "" : "s")} calling it" : ""),
            changes);
    }

    /// <summary>The text of a file in the solution.</summary>
    private async Task<SourceText?> TextOfAsync(string path, CancellationToken ct)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.Ordinal));

        return document is null
            ? null
            : await document.GetTextAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The method the caret is in, or why there is none.</summary>
    private async Task<(Document? Document, MethodBlockSyntax? Method, SourceText? Text,
                        RefactoringPreview? Refusal)>
        FindMethodAsync(string filePath, int position, CancellationToken ct)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
        {
            return (null, null, null, RefactoringPreview.Refused(
                Title, "That file is not part of the solution."));
        }

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return (null, null, null, RefactoringPreview.Refused(
                Title, "Changing a signature is only supported in Visual Basic so far."));
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null)
        {
            return (null, null, null,
                RefactoringPreview.Refused(Title, "The file could not be read."));
        }

        var token = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));

        if (token.Parent?.FirstAncestorOrSelf<MethodBlockSyntax>() is not { } method)
        {
            return (null, null, null, RefactoringPreview.Refused(
                Title, "Put the caret in a Sub or a Function."));
        }

        return (document, method, text, null);
    }
}
