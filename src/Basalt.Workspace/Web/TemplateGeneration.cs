using Basalt.Razor.Vb;

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
