namespace Basalt.Shell.Syntax;

/// <summary>
/// The grammar for Razor views written in Visual Basic.
///
/// The file is the one the VS Code extension ships, kept in one place so that
/// the IDE and the extension cannot drift into colouring the same view
/// differently. No such grammar exists anywhere else: ASP.NET Core never
/// supported .vbhtml, so nobody wrote one.
/// </summary>
public static class VbHtmlGrammar
{
    /// <summary>The scope the grammar registers itself under.</summary>
    public const string ScopeName = "text.html.vbhtml";

    /// <summary>Where the grammar is, or null when it is not beside the IDE.</summary>
    public static string? Path => Locate.Value;

    /// <summary>
    /// Always true: the grammar is carried inside the assembly.
    ///
    /// It used to be read from disk, and a stripped installation therefore
    /// had no grammar for .vbhtml — which fell back to the C# Razor grammar
    /// and coloured Visual Basic as C#. Embedding it removes the fallback
    /// rather than choosing a less wrong one.
    /// </summary>
    public static bool IsAvailable => true;

    /// <summary>
    /// The grammar itself.
    ///
    /// From disk when a copy is there, so an installation can carry a fixed
    /// one without a new build; from the assembly otherwise.
    /// </summary>
    public static string? Read() => ReadFromDisk() ?? Embedded.Value;

    /// <summary>The grammar built into this assembly.</summary>
    private static readonly Lazy<string?> Embedded = new(() =>
    {
        using var stream = typeof(VbHtmlGrammar).Assembly
            .GetManifestResourceStream("Basalt.Shell.Syntax.vbhtml.tmLanguage.json");

        if (stream is null) return null;

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });

    private static string? ReadFromDisk()
    {
        if (Path is not { } path) return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the grammar, whether running from a build or an installation.
    ///
    /// Looked for once: the answer cannot change while the IDE runs, and this
    /// is asked every time a file is opened.
    /// </summary>
    private static readonly Lazy<string?> Locate = new(() =>
    {
        const string relative = "extensions/vscode-vbrazor/syntaxes/vbhtml.tmLanguage.json";

        foreach (var directory in SearchRoots())
        {
            var candidate = System.IO.Path.Combine(directory, relative);

            if (File.Exists(candidate)) return candidate;
        }

        return null;
    });

    private static IEnumerable<string> SearchRoots()
    {
        // Beside the executable, where an installation puts it.
        yield return AppContext.BaseDirectory;

        // Or up the tree towards the repository root, when running from a build.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}
