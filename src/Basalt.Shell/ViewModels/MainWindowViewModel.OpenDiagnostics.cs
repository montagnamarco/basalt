namespace Basalt.Shell.ViewModels;

/// <summary>
/// The problems of a file shown as soon as it is open, not after the first
/// keystroke.
/// </summary>
/// <remarks>
/// Problems were recomputed only when typing paused, so a file opened with an
/// error in it showed none, in the Problems panel or as a squiggle, until the
/// user changed something. A file opened while the solution is still loading is
/// not in the workspace yet and has nothing to report, so every open file is
/// read again once the solution has loaded.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Whether a checker reads this file: Visual Basic source, and what a
    /// language provider claims, such as a Razor view.
    /// </summary>
    /// <remarks>
    /// Any other file, a .vbproj or a .resx, has its problems from the build
    /// alone. Checking it, when it was opened or edited, replaced its build
    /// errors with nothing: clicking a NETSDK error made it disappear.
    /// </remarks>
    private bool IsChecked(string filePath) =>
        Path.GetExtension(filePath).Equals(".vb", StringComparison.OrdinalIgnoreCase)
        || RoutedProviderFor(filePath) is not null;

    /// <summary>Set while a solution loads; its end checks every open file.</summary>
    private bool _loadingSolution;

    /// <summary>Recomputes the problems of the given documents shown in the text editor.</summary>
    private async Task RefreshDiagnosticsOfAsync(IEnumerable<EditorDocumentViewModel> documents)
    {
        var checkable = documents.Where(document => !document.OpenInDesigner).ToArray();

        foreach (var document in checkable)
        {
            try
            {
                await RefreshDiagnosticsAsync(document).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Started without anyone waiting for it: a failure is reported,
                // never thrown into the open that triggered it.
                AppendOutput($"[diagnostics] {ex.Message}");
            }
        }
    }
}
