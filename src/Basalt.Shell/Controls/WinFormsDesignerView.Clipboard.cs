using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private static WinFormsControlClipboard? s_controlClipboard;
    private static long s_clipboardGeneration;

    /// <summary>
    /// The designer whose Copy filled the clipboard, or empty after a Cut. A paste into that designer offsets its copies from
    /// their originals, as Visual Studio does on the same form; a paste into any other designer keeps their locations.
    /// </summary>
    private static Guid s_clipboardOrigin;
    private readonly Guid _designerIdentity = Guid.NewGuid();

    /// <summary>
    /// Test seam: when set, awaited right after a Cut stamps its generation and before
    /// its delete runs, so a test can force another designer's Copy to complete in
    /// between and observe which payload the generation guard below keeps. Production
    /// code never sets this.
    /// </summary>
    internal static Func<Task>? ClipboardRaceGateForTests;

    /// <summary>The clipboard payload currently shared by every open designer, for assertions.</summary>
    internal static WinFormsControlClipboard? ClipboardForTests => s_controlClipboard;

    /// <summary>Clears the static clipboard shared by every designer, so tests do not leak state into each other.</summary>
    internal static void ResetClipboardForTests()
    {
        s_controlClipboard = null;
        s_clipboardGeneration = 0;
        s_clipboardOrigin = Guid.Empty;
        ClipboardRaceGateForTests = null;
    }

    private void InitializeClipboardCommands()
    {
        _surface.CanPasteControls = () => s_controlClipboard is not null;
        _surface.CopyRequested += () => RunEditAsync(() => CopySelectionAsync(cut: false));
        _surface.CutRequested += () => RunEditAsync(() => CopySelectionAsync(cut: true));
        _surface.PasteRequested += () => RunEditAsync(PasteSelectionAsync);
    }

    /// <summary>
    /// Cut, Copy or Paste chosen through the window's own commands (the Edit menu, the palette or
    /// their shortcuts), which reach the window before this designer's surface sees the key.
    /// </summary>
    internal void RunClipboardCommand(bool cut, bool paste)
    {
        if (paste)
        {
            if (s_controlClipboard is not null) RunEditAsync(PasteSelectionAsync);
            return;
        }

        RunEditAsync(() => CopySelectionAsync(cut));
    }

    /// <summary>Drives the Copy/Cut command directly, bypassing the keyboard gesture, for tests.</summary>
    internal Task CopySelectionForTestsAsync(bool cut) => CopySelectionAsync(cut);

    private async Task CopySelectionAsync(bool cut)
    {
        if (_session is not { } session) return;
        var selection = _surface.SelectedControls.Where(control => !ReferenceEquals(control, session.Form)).ToArray();
        if (selection.Length == 0) return;
        var payload = session.CopyControls(selection);
        var generation = Interlocked.Increment(ref s_clipboardGeneration);
        if (cut)
        {
            await session.CheckPastableAsync(payload);
            if (ClipboardRaceGateForTests is { } gate) await gate();
            await DeleteSelectedControlsAsync(selection);
        }
        // A later Copy in another designer takes precedence over this awaited Cut.
        if (generation == Volatile.Read(ref s_clipboardGeneration))
        {
            s_controlClipboard = payload;
            s_clipboardOrigin = cut ? Guid.Empty : _designerIdentity;
        }
    }

    private async Task PasteSelectionAsync()
    {
        if (_session is not { } session || s_controlClipboard is not { } payload) return;
        var parent = _surface.SelectedControl;
        while (parent is not null && !WinFormsDesignerDrop.CanContain(parent))
            parent = parent.Parent;
        parent ??= session.Form;
        // Visual Studio offsets a copy pasted back onto its own form by the grid, so the copy is seen next to the original.
        var grid = _surface.GridSize;
        var gridStep = s_clipboardOrigin == _designerIdentity ? ((int Width, int Height)?)(grid.Width, grid.Height) : null;
        var names = await session.PasteControlsAsync(payload, parent, gridStep);
        _surface.SelectControls(names.Select(session.FindControl).OfType<Forms.Control>());
    }
}
