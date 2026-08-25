using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Basalt.Shell.Controls;

/// <summary>How a line differs from the committed version.</summary>
public enum LineChange { None, Added, Modified, Removed }

/// <summary>
/// The thin strip showing which lines differ from the last commit.
///
/// Built from git's own diff rather than a comparison made here, so what it
/// marks is exactly what would be committed.
/// </summary>
public sealed class GitChangeMargin : AbstractMargin
{
    private static readonly IBrush AddedBrush =
        new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));

    private static readonly IBrush ModifiedBrush =
        new SolidColorBrush(Color.FromRgb(0x37, 0x94, 0xFF));

    private static readonly IBrush RemovedBrush =
        new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E));

    private Dictionary<int, LineChange> _changes = [];

    protected override Size MeasureOverride(Size availableSize) => new(3, 0);

    internal IReadOnlyDictionary<int, LineChange> Changes => _changes;

    public void Show(IReadOnlyDictionary<int, LineChange> changes)
    {
        _changes = new Dictionary<int, LineChange>(changes);
        InvalidateVisual();
    }

    public void Clear() => Show(new Dictionary<int, LineChange>());

    /// <summary>
    /// Reads which lines a unified diff touches.
    ///
    /// A line that is both removed and added at the same place is a change
    /// rather than two events, which is how a reader sees it and how the
    /// margin should mark it.
    /// </summary>
    public static IReadOnlyDictionary<int, LineChange> FromDiff(string unifiedDiff)
    {
        var changes = new Dictionary<int, LineChange>();

        if (unifiedDiff.Length == 0) return changes;

        var line = 0;
        var removedPending = 0;

        foreach (var raw in unifiedDiff.Split('\n'))
        {
            var text = raw.TrimEnd('\r');

            if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                line = NewStartLine(text);
                removedPending = 0;
                continue;
            }

            if (text.Length == 0 || text.StartsWith("diff ", StringComparison.Ordinal) ||
                text.StartsWith("index ", StringComparison.Ordinal) ||
                text.StartsWith("--- ", StringComparison.Ordinal) ||
                text.StartsWith("+++ ", StringComparison.Ordinal) ||
                text.StartsWith('\\'))
            {
                continue;
            }

            switch (text[0])
            {
                case '-':
                    removedPending++;
                    break;

                case '+':
                    // Added straight after a removal is a modification.
                    changes[line] = removedPending > 0 ? LineChange.Modified : LineChange.Added;
                    if (removedPending > 0) removedPending--;
                    line++;
                    break;

                default:
                    // Removals with nothing added in their place leave a gap,
                    // marked on the line that now sits there.
                    if (removedPending > 0 && line > 0) changes[line] = LineChange.Removed;

                    removedPending = 0;
                    line++;
                    break;
            }
        }

        return changes;
    }

    private static int NewStartLine(string header)
    {
        foreach (var part in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith('+')) continue;

            var text = part[1..];
            var comma = text.IndexOf(',');

            return int.TryParse(comma < 0 ? text : text[..comma], out var value) ? value : 0;
        }

        return 0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var view = TextView;
        if (view?.VisualLinesValid != true || _changes.Count == 0) return;

        foreach (var visualLine in view.VisualLines)
        {
            var number = visualLine.FirstDocumentLine.LineNumber;

            if (!_changes.TryGetValue(number, out var change) || change == LineChange.None)
                continue;

            var top = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0], VisualYPosition.LineTop) - view.VerticalOffset;

            var height = visualLine.Height;

            var brush = change switch
            {
                LineChange.Added => AddedBrush,
                LineChange.Modified => ModifiedBrush,
                _ => RemovedBrush
            };

            // A removal has no line of its own, so it is drawn as a marker at
            // the top of the line that took its place.
            context.FillRectangle(
                brush,
                change == LineChange.Removed
                    ? new Rect(0, top, Bounds.Width, 2)
                    : new Rect(0, top, Bounds.Width, height));
        }
    }
}
