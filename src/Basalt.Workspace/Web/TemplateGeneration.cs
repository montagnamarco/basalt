using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Classic;

namespace Basalt.Workspace.Web;

/// <summary>
/// The Visual Basic a template compiles to, for the editor to ask about.
/// </summary>
/// <remarks>
/// One place, because the answer depends on which kind of template it is and
/// every provider needs the same answer. A .vbrazor is a Blazor component and
/// a .vbhtml is a view: they carry the same syntax and compile to different
/// classes, so generating the view shape for a component offered the members
/// of RazorPage — a completion list that looks right and names nothing the
/// class actually has.
/// </remarks>
internal static class TemplateGeneration
{
    public static bool IsPage(string? path) =>
        path is not null && path.EndsWith(".vbpage", StringComparison.OrdinalIgnoreCase);

    internal static bool IsInCode(string path, string text, int position)
    {
        if (!IsPage(path)) return VbHtmlCodeRegions.IsInCode(text, position);
        var page = VbPageParser.Parse(text, forEditing: true);
        return page.Parts.Concat(page.MemberBlocks).Any(part => part switch
        {
            VbPageParser.Code code => position >= code.Position && position <= code.Position + code.Text.Length,
            VbPageParser.Expression expression => position >= expression.Position && position <= expression.Position + expression.Text.Length,
            _ => false
        }) || page.ImportDirectives.Any(import =>
            position >= import.Position && position <= import.Position + import.Name.Length);
    }

    /// <summary>
    /// The caret carried by line, on a line the writer copied as it stands:
    /// a line of a statement block's body.
    /// </summary>
    /// <remarks>
    /// There the line mapping is exact and the span arithmetic is not: the
    /// writer re-indents a body running over several lines, so an offset
    /// measured from the body's start drifts by the indentation of every
    /// line before, and the caret after "= New" on the body's second line
    /// landed on the next one.
    ///
    /// Checked rather than assumed: the generated line must carry the
    /// template's text up to the caret, after its own indentation, or the
    /// answer is left to the span mapping. A line the writer rewrote, an
    /// expression put inside Write(...) say, never matches.
    /// </remarks>
    internal static int? StatementLineCaret(
        string path, string template, Generated generated, int position)
    {
        if (IsPage(path)) return null;

        var templateLineStart = template.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        if (position < templateLineStart) templateLineStart = 0;

        var templateLine = 1 + template.Take(templateLineStart).Count(character => character == '\n');

        if (generated.Map.ToGeneratedLine(templateLine) is not { } generatedLine) return null;

        var code = generated.Code;
        var generatedLineStart = StartOfLine(code, generatedLine);
        if (generatedLineStart < 0) return null;

        var generatedLineEnd = code.IndexOf('\n', generatedLineStart);
        if (generatedLineEnd < 0) generatedLineEnd = code.Length;
        if (generatedLineEnd > generatedLineStart && code[generatedLineEnd - 1] == '\r') generatedLineEnd--;

        var typed = template[templateLineStart..position].TrimStart(' ', '\t');

        // In the indentation there is no text to match, and answering with
        // the line's first token made hovering blank space describe "Dim".
        if (typed.Length == 0) return null;
        var content = code[generatedLineStart..generatedLineEnd].TrimStart(' ', '\t');
        var contentStart = generatedLineEnd - content.Length;

        if (content.StartsWith(typed, StringComparison.Ordinal))
            return contentStart + typed.Length;

        // The writer drops a statement's trailing spaces: the caret after
        // them goes to the end of the line, where they are put back.
        var word = typed.TrimEnd(' ', '\t');

        if (word.Length > 0 && content == word)
            return contentStart + word.Length;

        return null;
    }

    /// <summary>
    /// The template position a generated position came from, carried by line
    /// on a line the writer copied as it stands; null where it did not.
    /// </summary>
    /// <remarks>
    /// The way back from <see cref="StatementLineCaret"/>, and for the same
    /// reason: the span arithmetic ignores the writer's indentation of every
    /// body line, so a definition inside a multi-line block came back a
    /// line's indentation early per line. Checked the same way: the template
    /// line must carry the same text as the generated one.
    /// </remarks>
    internal static int? StatementLineOriginal(string path, string template, Generated generated, int generatedPosition)
    {
        if (IsPage(path)) return null;

        var code = generated.Code;
        if (generatedPosition < 0 || generatedPosition > code.Length) return null;

        var generatedLineStart = code.LastIndexOf('\n', Math.Max(0, generatedPosition - 1)) + 1;
        if (generatedPosition < generatedLineStart) generatedLineStart = 0;

        var generatedLine = 1 + code.Take(generatedLineStart).Count(character => character == '\n');

        if (generated.Map.OriginalLineOf(generatedLine) is not { } templateLine) return null;

        var indentEnd = generatedLineStart;
        while (indentEnd < code.Length && code[indentEnd] is ' ' or '\t') indentEnd++;

        // Inside the indentation nothing was written; at its end, the line's
        // first token, the position is as exact as anywhere else.
        if (generatedPosition < indentEnd) return null;

        var generatedLineEnd = code.IndexOf('\n', generatedLineStart);
        if (generatedLineEnd < 0) generatedLineEnd = code.Length;

        var templateLineStart = StartOfLine(template, templateLine);
        if (templateLineStart < 0) return null;

        var templateLineEnd = template.IndexOf('\n', templateLineStart);
        if (templateLineEnd < 0) templateLineEnd = template.Length;

        var content = template[templateLineStart..templateLineEnd].TrimStart(' ', '\t');
        var contentStart = templateLineEnd - content.Length;

        // The whole line copied as it stands, not only the part before the
        // position: at a line's first token nothing precedes it, and an empty
        // prefix matches any line at all.
        var writtenLine = code[indentEnd..generatedLineEnd].TrimEnd(' ', '\t', '\r');

        if (!string.Equals(content.TrimEnd(' ', '\t', '\r'), writtenLine, StringComparison.Ordinal))
            return null;

        return contentStart + (generatedPosition - indentEnd);
    }

    /// <summary>Where a 1-based line begins, or -1 when there is no such line.</summary>
    private static int StartOfLine(string text, int line)
    {
        var at = 0;

        for (var current = 1; current < line; current++)
        {
            at = text.IndexOf('\n', at) + 1;
            if (at == 0) return -1;
        }

        return at;
    }

    internal static IReadOnlyList<VbHtmlDiagnostic> ParseDiagnostics(string path, string text) =>
        IsPage(path) ? VbPageParser.Parse(text).Diagnostics : VbHtmlParser.Parse(text).Diagnostics;

    internal static async Task<Generated> ForAsync(
        string filePath, ViewHost host, string currentText,
        Func<string, string, CancellationToken, Task<IComponentCatalog?>>? askCatalog,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsPage(filePath))
        {
            var page = VbPageWriter.WriteWithMap(VbPageParser.Parse(currentText, forEditing: true),
                "GeneratedPage", "Basalt.Generated", filePath);
            return new Generated(page.Code, page.Map);
        }
        return await ForAsync(VbHtmlParser.Parse(currentText), filePath, host,
            currentText, askCatalog, ct).ConfigureAwait(false);
    }

    /// <summary>Whether this path is a Blazor component.</summary>
    public static bool IsComponent(string? path) =>
        path is not null
        && path.EndsWith(".vbrazor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The generated code and its mappings, whichever kind of template it is.
    /// </summary>
    /// <remarks>
    /// With no catalog: a named RenderFragment parameter is opened as a
    /// component that does not exist, @context inside a RenderFragment(Of T)
    /// is undeclared, and a generic component's type arguments are never
    /// inferred. <see cref="ForAsync"/> asks for one first; this overload
    /// stays for callers that have none to offer, or none worth the asking —
    /// a .vbhtml view, whose writer has no catalog to take in the first place.
    /// </remarks>
    public static Generated For(
        VbHtmlDocument document, string? filePath, ViewHost host, IComponentCatalog? catalog = null)
    {
        if (IsComponent(filePath))
        {
            // _Imports.vbrazor, as the build applies it to a component.
            ApplySharedFiles(document, filePath, "_Imports.vbrazor");

            var component = VbComponentWriter.WriteWithMap(
                document, "GeneratedComponent", "Basalt.Generated", filePath, catalog: catalog);

            return new Generated(component.Code, component.Map);
        }

        // The folder's _ViewImports, as the build applies them: without them
        // the editor underlined a service injected there, and a model type
        // imported there, as undeclared in every view that used them.
        ApplySharedFiles(document, filePath, ViewImports.FileName);

        var view = VbHtmlCodeWriter.WriteWithMap(
            document, "GeneratedView", "Basalt.Generated", filePath, host);

        return new Generated(view.Code, view.Map);
    }

    /// <summary>
    /// The generated code and its mappings, asking for a component's catalog
    /// first when a way to ask is offered.
    /// </summary>
    /// <remarks>
    /// Only a .vbrazor has anything to ask about: a view's writer takes no
    /// catalog, so a .vbhtml goes through <see cref="For"/> unchanged. The
    /// asking is a callback rather than a plain catalog because building one
    /// means reaching a compilation, which the caller — not this class —
    /// knows how to reach, and doing it lazily is what keeps a document
    /// opened outside a solution working exactly as before: no callback,
    /// no asking, no catalog, the writer behaves as it always did.
    /// </remarks>
    public static async Task<Generated> ForAsync(
        VbHtmlDocument document,
        string? filePath,
        ViewHost host,
        string currentText,
        Func<string, string, CancellationToken, Task<IComponentCatalog?>>? askCatalog,
        CancellationToken ct = default)
    {
        IComponentCatalog? catalog = null;

        if (askCatalog is not null && filePath is not null && IsComponent(filePath))
        {
            catalog = await askCatalog(filePath, currentText, ct).ConfigureAwait(false);
        }

        return For(document, filePath, host, catalog);
    }

    /// <summary>Generated code, with the map back to the template.</summary>
    public sealed record Generated(string Code, SourceMap Map);

    /// <summary>
    /// Applies every _ViewImports.vbhtml from the project folder down to the
    /// view's own, outermost first, as the source generator does.
    /// </summary>
    /// <remarks>
    /// Read from disk: the editor is asked about one document at a time and
    /// the shared files are other files. The walk stops at the folder holding
    /// the project file, which is where the build stops too.
    /// </remarks>
    /// <summary>
    /// Internal rather than private: <see cref="Basalt.Workspace.RoslynLanguageService"/>
    /// applies the same _Imports.vbrazor to every other component in the
    /// project while building its catalog, and a second copy of this walk
    /// would be a second chance for the two to disagree about which
    /// directives a component sees.
    /// </summary>
    internal static void ApplySharedFiles(VbHtmlDocument document, string? filePath, string sharedName)
    {
        if (filePath is null || !Path.IsPathRooted(filePath)) return;

        var folders = new List<string>();

        for (var folder = Path.GetDirectoryName(filePath); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            folders.Add(folder);

            if (IsProjectFolder(folder)) break;
        }

        folders.Reverse();

        var viewFolder = Path.GetDirectoryName(filePath)!;

        foreach (var folder in folders)
        {
            var shared = Path.Combine(folder, sharedName);

            // The view being edited may itself be a _ViewImports file.
            if (string.Equals(shared, filePath, StringComparison.OrdinalIgnoreCase)) continue;

            if (Parsed(shared) is not { } sharedDocument) continue;

            var below = viewFolder.Length > folder.Length
                ? string.Join(".", viewFolder[folder.Length..]
                    .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(folder => ViewNaming.Escape(ViewNaming.MakeClassName(folder))))
                : "";

            ViewImports.ApplyTo(document, sharedDocument, below);

            // A component takes its layout from _Imports as well; a view's comes
            // from _ViewStart, which MVC runs.
            if (sharedName != ViewImports.FileName) ViewImports.ApplyLayoutTo(document, sharedDocument);
        }
    }

    private static bool IsProjectFolder(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.vbproj").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A shared file parsed, cached until it changes on disk.</summary>
    private static VbHtmlDocument? Parsed(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var written = File.GetLastWriteTimeUtc(path);

            if (SharedFiles.TryGetValue(path, out var cached) && cached.Written == written)
                return cached.Document;

            var document = VbHtmlParser.Parse(File.ReadAllText(path));

            SharedFiles[path] = (written, document);

            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parsed shared files by path. They are read on every question about
    /// every view below them, and reading them each time would be a disk read
    /// per keystroke.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Written, VbHtmlDocument Document)>
        SharedFiles = new(StringComparer.OrdinalIgnoreCase);
}
