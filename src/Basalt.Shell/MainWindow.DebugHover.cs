using System.Runtime.CompilerServices;
using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>
/// Connects an editor's real pointer movements to a real stopped debug
/// session, so resting on a variable while paused shows its value.
///
/// <see cref="DebugHoverEvaluator"/> already did all of this on its own, but
/// nothing ever constructed one for a real editor: the window built its
/// breakpoint margins and its Locals panel from the same session, yet hover
/// evaluation stayed dead code. Kept in its own file, per AGENTS.md, rather
/// than growing MainWindow.axaml.cs.
/// </summary>
public partial class MainWindow
{
    // Keyed by the editor, weakly: a closed tab's tip goes with its editor, and a rename (which
    // retargets the same editor) needs no bookkeeping here.
    private readonly ConditionalWeakTable<CodeEditor, DebugHoverEvaluator> _dataTips = new();

    private bool _dataTipsFollowTheSession;

    /// <summary>Wires one editor's pointer to the session.</summary>
    private void ConnectDebugHover(CodeEditor editor)
    {
        FollowTheSessionWithDataTips();

        editor.HoverYieldsToDebugger = () => _debug.IsPaused;

        _dataTips.AddOrUpdate(editor, new DebugHoverEvaluator(
            editor.TextEditorForDataTips,
            expression => _debug.EvaluateAsync(expression),
            () => _debug.IsPaused));
    }

    /// <summary>
    /// A value read while stopped is stale the moment execution moves on: continuing, stepping or
    /// stopping closes every open tip, even if the pointer never moved.
    /// </summary>
    private void FollowTheSessionWithDataTips()
    {
        if (_dataTipsFollowTheSession) return;
        _dataTipsFollowTheSession = true;

        _debug.Resumed += (_, _) => HideDataTips();
        _debug.Exited += (_, _) => HideDataTips();
    }

    private void HideDataTips()
    {
        foreach (var (_, tip) in _dataTips) tip.Hide();
    }

    /// <summary>The data tip wired to a file's editor, for tests.</summary>
    internal DebugHoverEvaluator? HoverEvaluatorForTests(string filePath)
    {
        foreach (var (editor, tip) in _dataTips)
        {
            if (string.Equals(editor.FilePath, filePath, System.StringComparison.Ordinal)) return tip;
        }

        return null;
    }
}
