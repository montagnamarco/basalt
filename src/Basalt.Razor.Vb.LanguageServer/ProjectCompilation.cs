using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Workspace;
using Basalt.Workspace.Languages;
using Basalt.Workspace.Web;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// The compiler behind the server's answers.
/// </summary>
/// <remarks>
/// Without one the server can describe markup and nothing else: asked about
/// <c>@Model.</c> it has no idea what the model is, because only a compilation
/// knows. Basalt answers those questions by generating the view to Visual
/// Basic and asking Roslyn about the generated position; this is the same
/// delegation, running outside the IDE so that Visual Studio, Rider and VS
/// Code get the same answers.
///
/// The solution is opened in the background. A request arriving before it is
/// ready is answered from the markup alone rather than made to wait: an editor
/// that pauses is worse than one that knows less for a second.
/// </remarks>
public sealed class ProjectCompilation : IDisposable
{
    private readonly RoslynLanguageService _roslyn = new();
    private readonly RoslynFormattingService _formatting = new();
    private Task? _loading;
    private string? _target;

    /// <summary>Whether the solution has finished loading.</summary>
    public bool IsReady { get; private set; }

    /// <summary>What went wrong, when the solution could not be opened.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The provider the handlers ask, with the compiler behind it when there
    /// is one.
    /// </summary>
    public ILanguageProvider Provider { get; private set; } =
        WebLanguageProviders.VbRazor;

    /// <summary>
    /// Starts opening the solution or project under a folder.
    /// </summary>
    /// <remarks>
    /// The root comes from the client at initialisation. Nothing is awaited
    /// here: a large solution takes seconds to load, and the editor must not
    /// be blocked while it does.
    /// </remarks>
    /// <summary>
    /// Where to look when the client names no folder.
    /// </summary>
    /// <remarks>
    /// A seam for the tests: the real answer is the process's working
    /// directory, and a test that sets that would move it for every other test
    /// sharing the process.
    /// </remarks>
    internal Func<string> WorkingDirectory { get; init; } = Directory.GetCurrentDirectory;

    public void StartLoading(string? rootPath, CancellationToken ct = default)
    {
        // The working directory, when the client names no folder. An editor
        // starts the server inside the project it opened, so this is nearly
        // always the right answer — and it is certainly better than refusing
        // to look, which is what left completion knowing no types at all in
        // an editor that sends neither rootUri nor workspaceFolders.
        if (string.IsNullOrWhiteSpace(rootPath)) rootPath = WorkingDirectory();

        if (!Directory.Exists(rootPath))
        {
            Problem = $"the folder given does not exist: {rootPath}";
            return;
        }

        var target = FindSolutionOrProject(rootPath!);

        if (target is null)
        {
            Problem = $"no solution or Visual Basic project was found under {rootPath}";
            return;
        }

        _target = target;
        _loading = LoadAsync(target, ct);
    }

    /// <summary>Waits for the solution, for tests and for shutdown.</summary>
    public Task Loaded => _loading ?? Task.CompletedTask;

    /// <summary>
    /// Tells the compilation that a file on disk changed.
    /// </summary>
    /// <remarks>
    /// A view's model lives in a .vb file, and its members are what the server
    /// answers with: without this the answers describe a class as it was when
    /// the solution was opened, which is worse than not answering because it
    /// looks current.
    /// </remarks>
    public async Task FileChangedAsync(string path, CancellationToken ct = default)
    {
        if (!IsReady) return;

        // A project file changing means references or files came and went, so
        // the whole solution is opened again; a source file only needs its own
        // text refreshed, which is far cheaper and happens on every keystroke
        // in another editor.
        if (path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            IsReady = false;
            _loading = LoadAsync(_target!, ct);
            return;
        }

        if (!path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(path)) return;

        try
        {
            await _roslyn
                .UpdateDocumentAsync(path, File.ReadAllText(path), ct)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Being written as we read it. The next change brings us the
            // finished text, so there is nothing useful to report.
        }
    }

    private async Task LoadAsync(string target, CancellationToken ct)
    {
        try
        {
            await _roslyn.OpenSolutionAsync(target, ct).ConfigureAwait(false);

            // Through the very provider the IDE uses, so the two cannot
            // drift apart: a conversion written twice is a conversion that
            // ends up disagreeing.
            var vb = RoslynLanguageProvider.CreateVisualBasic(_roslyn, _formatting);

            // Straight to the service, passing the generated text as the
            // current text of a file the solution does not have: that is what
            // makes it a scratch document carrying the solution's references.
            // Going through the providers instead passes currentText as
            // Nothing, the scratch document is never made, and every question
            // comes back empty — which reads as "nothing to suggest here".
            const string generatedPath = "__vbhtml_generated.vb";

            Provider = WebLanguageProviders.VbRazorAsking(
                ask: async (text, position, token) =>
                {
                    var items = await _roslyn
                        .GetCompletionsAsync(generatedPath, position, text, token)
                        .ConfigureAwait(false);

                    return items.Select(RoslynCompletionProvider.Convert).ToList();
                },
                askQuickInfo: async (text, position, token) =>
                {
                    var info = await _roslyn
                        .GetQuickInfoAsync(generatedPath, position, text, token)
                        .ConfigureAwait(false);

                    return info is null ? null : new QuickInfo(info);
                },
                askDiagnostics: (text, token) =>
                    _roslyn.GetDiagnosticsAsync(generatedPath, text, token),
                askSignature: (text, position, token) =>
                    _roslyn.GetSignatureHelpAsync(generatedPath, position, text, token),

                // Out of the view, into the model class or any .vb in the
                // solution: without these the server answered only for names
                // declared in the view itself, and references always came
                // back empty.
                askDefinition: async (text, position, token) =>
                {
                    var found = await _roslyn
                        .GoToDefinitionAsync(generatedPath, position, text, token)
                        .ConfigureAwait(false);

                    return found is { } at
                        ? new SourceLocation(at.FilePath, SourceRange.At(new SourcePosition(at.Line, at.Column)))
                        : null;
                },
                askReferences: (text, position, token) =>
                    _roslyn.FindReferencesAsync(generatedPath, position, text, token),

                // The solution that was just opened is a web one, so the view
                // is generated as ASP.NET Core would: with the standalone
                // shape it inherits a base class the project has never heard
                // of and Roslyn resolves nothing at all.
                host: ViewHost.AspNetCore,

                // A .vbrazor's own component catalog, learned from the
                // solution the way the build learns it: without this Visual
                // Studio, Rider and VS Code all saw a named RenderFragment
                // parameter opened as a component that does not exist.
                askCatalog: (templatePath, currentText, token) =>
                    _roslyn.GetComponentCatalogAsync(templatePath, currentText, token));

            IsReady = true;
        }
        catch (OperationCanceledException)
        {
            // Shutting down, which is not a problem to report.
        }
        catch (Exception ex)
        {
            // Said rather than swallowed: a server that answers less well and
            // does not say why sends the reader looking at their own project.
            Problem = $"the solution at {target} could not be opened: {ex.Message}";
        }
    }

    /// <summary>
    /// The solution to open, or the project when there is no solution.
    /// </summary>
    /// <remarks>
    /// A solution first, because a view's model commonly lives in a project
    /// other than the web one, and only the solution ties them together.
    /// </remarks>
    private static string? FindSolutionOrProject(string root)
    {
        foreach (var pattern in new[] { "*.slnx", "*.sln" })
        {
            var found = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
            if (found.Length > 0) return found[0];
        }

        var projects = Directory.GetFiles(root, "*.vbproj", SearchOption.AllDirectories);

        return projects.Length > 0 ? projects[0] : null;
    }

    public void Dispose() => _roslyn.Dispose();
}
