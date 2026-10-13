using System.Collections.Specialized;
using Avalonia;
using Avalonia.Threading;

namespace Basalt.Shell.Controls;

/// <summary>
/// The underlines follow the problems wherever they come from.
/// </summary>
/// <remarks>
/// The editor redrew its underlines only after its own recomputation, when
/// typing paused. Problems found when the file opened, or by a build, reached
/// the Problems panel and left the text unmarked until the next keystroke.
/// Followed only while the editor is shown: it has no other end of life, and
/// a subscription kept after its tab closed would keep it alive.
/// </remarks>
public sealed partial class CodeEditor
{
    internal IReadOnlyList<Basalt.Core.Model.IdeDiagnostic> UnderlinedForTests => _squiggles.Shown;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _shell.Diagnostics.CollectionChanged += OnProblemsChanged;
        ShowDiagnostics();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _shell.Diagnostics.CollectionChanged -= OnProblemsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private bool _underlinesPending;

    /// <summary>
    /// Redraws once for a burst of changes: a build adds its problems one at a
    /// time, and redrawing every editor for each was quadratic in their number.
    /// </summary>
    private void OnProblemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_underlinesPending) return;
        _underlinesPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _underlinesPending = false;
            ShowDiagnostics();
        }, DispatcherPriority.Background);
    }
}
