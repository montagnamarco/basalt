using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Moving a type into a file named after it.
///
/// Two files change: the one the type leaves and the one it arrives in. The
/// preview shows both, which is the reason a preview exists — a refactoring
/// that writes a file the user did not know about is one they cannot undo.
/// </summary>
public sealed class MoveTypeToFileRefactoring
{
    private const string Title = "Move Type to File";

    private readonly Solution _solution;

    public MoveTypeToFileRefactoring(Solution solution) => _solution = solution;

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(Title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                Title, "Moving a type is only supported in Visual Basic so far.");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is not CompilationUnitSyntax unit)
            return RefactoringPreview.Refused(Title, "The file could not be read.");

        var token = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));

        if (token.Parent?.FirstAncestorOrSelf<TypeBlockSyntax>() is not { } type)
            return RefactoringPreview.Refused(Title, "Put the caret in a type.");

        // The outermost type: moving a nested one would take it out of the
        // type it belongs to, which is a different change.
        while (type.Parent?.FirstAncestorOrSelf<TypeBlockSyntax>() is { } outer) type = outer;

        var name = type.BlockStatement.Identifier.ValueText;

        var types = unit.DescendantNodes()
            .OfType<TypeBlockSyntax>()
            .Where(t => t.Parent?.FirstAncestorOrSelf<TypeBlockSyntax>() is null)
            .ToList();

        if (types.Count <= 1)
        {
            return RefactoringPreview.Refused(
                Title, $"'{name}' is the only type in this file.");
        }

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var destination = Path.Combine(directory, $"{name}.vb");

        if (File.Exists(destination))
        {
            return RefactoringPreview.Refused(
                Title, $"{Path.GetFileName(destination)} already exists.");
        }

        var lineEnding = text.ToString().Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        // The imports come along: the type may need them, and an unused
        // import in the new file is a smaller problem than a missing one.
        var imports = string.Join(lineEnding, unit.Imports.Select(i => i.ToString().TrimEnd()));

        var moved = new System.Text.StringBuilder();

        if (imports.Length > 0) moved.Append(imports).Append(lineEnding).Append(lineEnding);

        moved.Append(WithNamespace(type, lineEnding)).Append(lineEnding);

        // The whole lines the type sat on, so no blank line is left behind.
        var from = text.Lines.GetLineFromPosition(type.SpanStart).Start;
        var to = text.Lines.GetLineFromPosition(type.Span.End).EndIncludingLineBreak;

        var remaining = text.Replace(TextSpan.FromBounds(from, to), "").ToString();

        return new RefactoringPreview(
            $"Move '{name}' to {Path.GetFileName(destination)}",
            [
                new FileChangePreview(filePath, text.ToString(), remaining),
                new FileChangePreview(destination, "", moved.ToString())
            ]);
    }

    /// <summary>
    /// The type, inside the namespace it was in.
    ///
    /// A type moved out of its namespace is a type with a different full
    /// name, which every use of it would then fail to find.
    /// </summary>
    private static string WithNamespace(TypeBlockSyntax type, string lineEnding)
    {
        if (type.FirstAncestorOrSelf<NamespaceBlockSyntax>() is not { } enclosing)
            return Dedent(type.ToString(), lineEnding);

        var name = enclosing.NamespaceStatement.Name.ToString();

        var body = string.Join(lineEnding,
            Dedent(type.ToString(), lineEnding)
                .Split(lineEnding)
                .Select(line => line.Length == 0 ? line : "    " + line));

        return $"Namespace {name}{lineEnding}{body}{lineEnding}End Namespace";
    }

    /// <summary>
    /// The type's text with the indentation it had inside the old file
    /// removed, so it starts at the margin of the new one.
    /// </summary>
    private static string Dedent(string text, string lineEnding)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var indent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join(lineEnding, lines.Select(
            l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }
}
