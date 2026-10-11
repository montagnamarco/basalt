using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Keeps the designer on the controls of the project's latest successful build: the context
/// that loads the build's assemblies, the toolbox group that lists them, and the Output lines
/// for project code that failed.
/// </summary>
internal sealed partial class WinFormsDesignerView
{
    private readonly Func<Action<string, string, long>, IDisposable>? _subscribeToBuilds;
    private readonly Action<string>? _writeOutput;
    private IDisposable? _buildSubscription;
    private WinFormsDesignAssemblyContext? _designContext;
    private int _designContextRequest;

    /// <summary>
    /// The id of the build whose context is in use. A subscription replays the last build every time the designer is
    /// shown again; a replay of this id is not loaded a second time. Ids start at 1, so 0 means no build yet.
    /// </summary>
    private long _appliedBuildId;

    /// <summary>
    /// A context loaded while the view was busy, with the id of its build. It waits here and is applied once the view
    /// is idle again (see <see cref="ApplyPendingDesignContext"/>).
    /// </summary>
    private (WinFormsDesignAssemblyContext Context, long BuildId)? _pendingDesignContext;

    /// <summary>The Forms toolbox entries, which do not change with the project; built once.</summary>
    private static readonly WinFormsToolboxItem[] BuiltInToolboxItems = BuildBuiltInToolboxItems();

    /// <summary>
    /// Called with the project and the output assembly of each successful build, and of the last one
    /// that succeeded before this designer opened, so a designer opened after a build still sees it.
    /// </summary>
    /// <remarks>
    /// A failed build raises nothing, so the context already loaded stays and the controls keep
    /// showing. The new context is read on a background thread; the preview and toolbox change on the
    /// interface thread, and never while an edit or a modal runs: the edit's result is written to the session it
    /// started on, so the swap waits for it (see <see cref="ApplyPendingDesignContext"/>). The previous
    /// context is unloaded only after the controls it created are disposed, which is what lets the next
    /// build overwrite its files. A swap that fails keeps the previous context, releases the new one, and does
    /// not record the build as applied, so the same build can be applied again.
    /// </remarks>
    private async void OnProjectBuilt(string builtProject, string outputAssemblyPath, long buildId)
    {
        var project = WinFormsProjectResources.FindProject(_document.FilePath);
        if (_disposed || project is null || !SamePath(builtProject, project)) return;

        // Only a build the designer has applied is skipped. The id, not the output's path and write time, identifies
        // a build: a second build can leave the output file unchanged when only a referenced library changed.
        if (buildId == _appliedBuildId) return;

        var request = ++_designContextRequest;
        WinFormsDesignAssemblyContext context;
        try
        {
            context = await Task.Run(() => WinFormsDesignAssemblyContext.Open(outputAssemblyPath));
        }
        catch (Exception exception)
        {
            // The context already loaded keeps the designer working. The build is not recorded, so it can be asked for again.
            ReportDesignContextFailure(exception);
            return;
        }

        if (_disposed || request != _designContextRequest)
        {
            context.Unload();
            return;
        }

        if (IsBusyForDesignContext)
        {
            QueuePendingDesignContext(context, buildId);
            return;
        }

        ApplyDesignContext(context, buildId);
    }

    /// <summary>
    /// Whether a swap of the design context has to wait: an edit runs, or a modal opened from the designer is open.
    /// The edit or modal holds the session it started on, and its result is written there.
    /// </summary>
    private bool IsBusyForDesignContext => _busy || _openModals > 0;

    /// <summary>
    /// Makes the context the designer's: the preview, the surface and the toolbox then show its controls. If the
    /// session cannot build its preview on the context, the previous one stays in use and the new one is released,
    /// and the build is not recorded as applied.
    /// </summary>
    private void ApplyDesignContext(WinFormsDesignAssemblyContext context, long buildId)
    {
        var previous = _designContext;
        _designContext = context;
        _surface.DesignContext = context;
        try
        {
            _session?.UseDesignContext(context);
        }
        catch (Exception exception)
        {
            _designContext = previous;
            _surface.DesignContext = previous;
            context.Unload();
            ReportDesignContextFailure(exception);
            return;
        }

        _appliedBuildId = buildId;
        LogLibraryFailures(context);

        // The preview has disposed the controls of the previous context by now.
        try
        {
            RebuildToolbox();
            LogUserCodeFailures();
        }
        catch (Exception exception)
        {
            // The previous context is unloaded below. A toolbox that still lists its items would hold types of an
            // unloaded assembly, so it is shown with the Forms entries only until the next build.
            ShowToolbox([]);
            ShowError(exception);
        }
        finally { previous?.Unload(); }
    }

    /// <summary>Says once, on the status line and in Output, why the project's controls could not be loaded.</summary>
    private void ReportDesignContextFailure(Exception exception)
    {
        var message = Localizer.Get(StringKeys.WinFormsDesignerDesignContextFailed, exception.GetBaseException().Message);
        _status.Text = message;
        _writeOutput?.Invoke(message);
    }

    /// <summary>
    /// Writes each library the build could not read or describe, and why the build's library list could not be read,
    /// to Output once for the build. The other libraries and the project's controls are listed as usual.
    /// </summary>
    private void LogLibraryFailures(WinFormsDesignAssemblyContext context)
    {
        if (_writeOutput is null) return;
        if (context.DependencyListProblem is { } problem) _writeOutput(problem);
        foreach (var failure in context.FailedLibraries)
            _writeOutput(Localizer.Get(StringKeys.WinFormsDesignerLibraryNotListed, failure.Name, failure.Message));
    }

    /// <summary>
    /// Keeps the newest context for the edit that is running. An older one waiting there is released, since
    /// only the latest build matters.
    /// </summary>
    private void QueuePendingDesignContext(WinFormsDesignAssemblyContext context, long buildId)
    {
        var replaced = _pendingDesignContext;
        _pendingDesignContext = (context, buildId);
        replaced?.Context.Unload();
    }

    /// <summary>
    /// Applies the context queued while the view was busy, once the edit has completed and no modal is open. Called
    /// when an edit completes and when the last modal closes, so whichever ends last applies it.
    /// </summary>
    private void ApplyPendingDesignContext()
    {
        if (_pendingDesignContext is not { } pending || IsBusyForDesignContext) return;
        _pendingDesignContext = null;

        if (_disposed) pending.Context.Unload();
        else ApplyDesignContext(pending.Context, pending.BuildId);
    }

    private void SubscribeToBuilds() => _buildSubscription ??= _subscribeToBuilds?.Invoke(OnProjectBuilt);

    /// <summary>The id of the build whose context is in use; for the designer's tests.</summary>
    internal long AppliedBuildIdForTests => _appliedBuildId;

    private void UnsubscribeFromBuilds()
    {
        _buildSubscription?.Dispose();
        _buildSubscription = null;
    }

    /// <summary>
    /// The toolbox: the Forms entries, then the project's own controls under "&lt;Project&gt; Components" and each
    /// library's under "&lt;Library&gt; Components", refreshed after every build.
    /// </summary>
    /// <remarks>
    /// Reads only what the context described when it was opened (see <see cref="WinFormsDesignAssemblyContext.Open"/>),
    /// so no assembly is read on the interface thread here.
    /// </remarks>
    private void RebuildToolbox()
    {
        BeforeToolboxRebuildForTests?.Invoke();
        ShowToolbox(ProjectToolboxItems());
    }

    /// <summary>Shows the Forms entries and the given project items, and selects Pointer.</summary>
    private void ShowToolbox(IReadOnlyList<WinFormsToolboxItem> projectItems)
    {
        var items = WinFormsToolboxCatalog.Order(BuiltInToolboxItems.Concat(projectItems));
        _toolboxPanel.Show(items, item => item.Category, item => [item.DisplayName], WinFormsToolboxVisuals.CreateRow);

        // Pointer is the toolbox's resting state: nothing is pending to place
        // until a control is chosen, just as VB6 and Visual Studio start out.
        _toolbox.SelectedItem = WinFormsToolboxCatalog.Pointer;
    }

    /// <summary>
    /// The controls the project and the libraries it uses define: the project's own under "&lt;Project&gt; Components",
    /// and each referenced library's under "&lt;Library&gt; Components". Empty until a build of the project has loaded.
    /// </summary>
    /// <remarks>
    /// Only controls are listed. Components are not listed yet: the designer's document reads a component as a
    /// control unless the tray names it, so a component listed here would be placed and then not found again
    /// (see WinFormsDesignerDocument.Components). They are listed once the document can tell them apart.
    /// </remarks>
    private IReadOnlyList<WinFormsToolboxItem> ProjectToolboxItems()
    {
        if (_designContext is null || WinFormsProjectResources.FindProject(_document.FilePath) is not { } project)
            return [];

        var items = new List<WinFormsToolboxItem>();
        var projectCategory = Localizer.Get(StringKeys.WinFormsDesignerProjectComponents, Path.GetFileNameWithoutExtension(project));
        items.AddRange(WinFormsToolboxCatalog.UnderCategory(_designContext.ProjectControls, projectCategory));
        foreach (var library in _designContext.ReferencedLibraries())
        {
            var category = Localizer.Get(StringKeys.WinFormsDesignerLibraryComponents, library.Name);
            items.AddRange(WinFormsToolboxCatalog.UnderCategory(library.Controls, category));
        }
        return items;
    }

    /// <summary>Writes each failure of the project's code to Output once, however many times the preview was rebuilt.</summary>
    private void LogUserCodeFailures()
    {
        if (_session is null || _writeOutput is null) return;
        foreach (var failure in _session.TakeUnreportedUserCodeFailures()) _writeOutput(failure);
    }

    /// <summary>The context the preview is built on; for the designer's tests.</summary>
    internal WinFormsDesignAssemblyContext? CurrentDesignContextForTests => _designContext;

    /// <summary>Runs first in every toolbox rebuild, so a test can make the rebuild fail; for the designer's tests.</summary>
    internal Action? BeforeToolboxRebuildForTests { get; set; }

    /// <summary>Rebuilds the toolbox from the current context, as a build does; for the designer's tests.</summary>
    internal void RebuildToolboxForTests() => RebuildToolbox();

    /// <summary>Whether a context waits for the running edit to complete; for the designer's tests.</summary>
    internal bool HasPendingDesignContextForTests => _pendingDesignContext is not null;

    /// <summary>Unsubscribes and subscribes again, as the view does when it is shown again; for the designer's tests.</summary>
    internal void ResubscribeToBuildsForTests()
    {
        UnsubscribeFromBuilds();
        SubscribeToBuilds();
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static WinFormsToolboxItem[] BuildBuiltInToolboxItems() =>
        [.. WinFormsToolboxCatalog.Describe(typeof(Forms.Control).Assembly, typeof(Forms.Control))
            .Where(item => !typeof(Forms.Form).IsAssignableFrom(item.ControlType)
                && item.ControlType != typeof(Forms.WebBrowser) && item.ControlType != typeof(Forms.AxHost)
                && item.ControlType != typeof(Forms.ContextMenuStrip))
            .Concat(new[] { typeof(Forms.Timer), typeof(Forms.ImageList), typeof(Forms.ToolTip), typeof(Forms.ContextMenuStrip),
                    typeof(Forms.OpenFileDialog), typeof(Forms.SaveFileDialog), typeof(Forms.FolderBrowserDialog),
                    typeof(Forms.ColorDialog), typeof(Forms.FontDialog), typeof(System.IO.Ports.SerialPort) }
                .Select(type => new WinFormsToolboxItem(type)))
            .Append(WinFormsToolboxCatalog.Pointer)];
}
