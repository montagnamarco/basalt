using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Renaming a symbol inside the Razor views that use it.
///
/// Roslyn cannot do this part: a .vbhtml is not a document in the solution,
/// so the renamer neither sees it nor edits it. Without this, renaming a
/// model property leaves every view still naming the old one, and the break
/// only shows up at build time.
///
/// Each view is generated to Visual Basic, asked about in that form, and the
/// positions that come back travel through the source map to the template.
/// A use that will not map is left alone rather than guessed at.
/// </summary>
public sealed class ViewRename
{
    private readonly Solution _solution;

    public ViewRename(Solution solution) => _solution = solution;

    /// <summary>
    /// The views that would change, and what they would become.
    ///
    /// Empty when nothing uses the symbol, which is the common case and
    /// costs one generation per view to establish.
    /// </summary>
    public async Task<IReadOnlyList<FileChangePreview>> PreviewAsync(
        ISymbol symbol,
        string newName,
        IEnumerable<string> viewPaths,
        CancellationToken ct = default)
    {
        var changes = new List<FileChangePreview>();

        foreach (var path in viewPaths)
        {
            ct.ThrowIfCancellationRequested();

            string original;

            try
            {
                original = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A view that cannot be read is a view that cannot be renamed;
                // the rest of the rename is still worth offering.
                continue;
            }

            var updated = await RewriteAsync(path, original, symbol, newName, ct)
                .ConfigureAwait(false);

            if (updated is not null && !string.Equals(updated, original, StringComparison.Ordinal))
                changes.Add(new FileChangePreview(path, original, updated));
        }

        return changes;
    }

    /// <summary>
    /// One view with every mappable use of the symbol renamed, or null when
    /// it does not use the symbol at all.
    /// </summary>
    private async Task<string?> RewriteAsync(
        string path, string text, ISymbol symbol, string newName, CancellationToken ct)
    {
        // The template has to name the symbol somewhere for any of this to be
        // worth doing: generating and compiling every view to discover
        // otherwise would be slow for no gain.
        if (!text.Contains(symbol.Name, StringComparison.Ordinal)) return null;

        var generated = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(text), "GeneratedView", "Basalt.Generated", path);

        var host = _solution.Projects
            .FirstOrDefault(p => p.Language == LanguageNames.VisualBasic);

        if (host is null) return null;

        var document = host.AddDocument("__BasaltViewRename.vb", SourceText.From(generated.Code));

        var found = await SymbolFinder
            .FindReferencesAsync(symbol, document.Project.Solution, ct)
            .ConfigureAwait(false);

        var generatedText = await document.GetTextAsync(ct).ConfigureAwait(false);

        // Where in the template each use sits. Collected first, applied
        // afterwards from the end, so earlier edits do not shift later spans.
        var spans = new List<TextSpan>();
        var templateText = SourceText.From(text);

        foreach (var reference in found)
        {
            foreach (var use in reference.Locations)
            {
                if (use.Document.Id != document.Id) continue;

                var inGenerated = use.Location.SourceSpan;

                var line = LineOf(generated, generatedText, inGenerated.Start);

                if (line is not { } templateLine) continue;

                spans.AddRange(NamesOn(templateText, templateLine, symbol.Name));
            }
        }

        if (spans.Count == 0) return null;

        var edited = text;

        foreach (var span in spans
                     .DistinctBy(s => s.Start)
                     .OrderByDescending(s => s.Start))
        {
            edited = edited.Remove(span.Start, span.Length).Insert(span.Start, newName);
        }

        return edited;
    }

    /// <summary>
    /// The template line a generated offset came from.
    ///
    /// The line, not the offset: <c>#ExternalSource</c> carries a line number
    /// and nothing finer, so a mapped region and the template text behind it
    /// are different lengths — <c>Write(p.Name)</c> against
    /// <c>&lt;p&gt;@p.Name&lt;/p&gt;</c>. Arithmetic inside a region drifts,
    /// and lands a few characters past the name.
    /// </summary>
    private static int? LineOf(
        VbHtmlCodeWriter.Generated generated, SourceText generatedText, int offset)
    {
        if (offset < 0 || offset > generatedText.Length) return null;

        var generatedLine = generatedText.Lines.GetLinePosition(offset).Line + 1;

        return generated.Map.OriginalLineOf(generatedLine);
    }

    /// <summary>
    /// Every whole-word occurrence of the name on one template line.
    ///
    /// Whole-word because the line is all the map can give: renaming the
    /// "Name" inside "UserName" would corrupt the template silently.
    /// </summary>
    private static IEnumerable<TextSpan> NamesOn(SourceText text, int line, string name)
    {
        var index = line - 1;

        if (index < 0 || index >= text.Lines.Count) yield break;

        var textLine = text.Lines[index];
        var content = text.ToString(textLine.Span);

        var at = content.IndexOf(name, StringComparison.Ordinal);

        while (at >= 0)
        {
            if (IsWholeWord(content, at, name.Length))
                yield return new TextSpan(textLine.Start + at, name.Length);

            at = content.IndexOf(name, at + 1, StringComparison.Ordinal);
        }
    }

    /// <summary>Whether a match is a name of its own, not part of a longer one.</summary>
    private static bool IsWholeWord(string text, int start, int length)
    {
        if (start > 0 && IsNameCharacter(text[start - 1])) return false;

        var after = start + length;

        return after >= text.Length || !IsNameCharacter(text[after]);
    }

    private static bool IsNameCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// The Razor views belonging to a solution.
    ///
    /// Found on disk beside the projects, because they are not solution
    /// documents: nothing in the workspace lists them.
    /// </summary>
    public static IReadOnlyList<string> ViewsOf(Solution solution)
    {
        var roots = solution.Projects
            .Select(p => Path.GetDirectoryName(p.FilePath))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var views = new List<string>();

        foreach (var root in roots)
        {
            try
            {
                views.AddRange(Directory.EnumerateFiles(root!, "*.vbhtml",
                    SearchOption.AllDirectories));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A directory that cannot be listed contributes no views.
            }
        }

        return views.Distinct(StringComparer.Ordinal).ToList();
    }
}
