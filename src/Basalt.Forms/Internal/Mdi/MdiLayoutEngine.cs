using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>
/// What Form.LayoutMdi does to the children of an MdiClient: Windows'
/// CascadeWindows, TileWindows and ArrangeIconicWindows in the client area.
/// Cascading and tiling restore maximized children first and leave minimized
/// ones where they are.
/// </summary>
internal static class MdiLayoutEngine
{
    /// <summary>How far each cascaded window steps down and right: a title bar and a sizing frame.</summary>
    private const int CascadeStep = NonClientArea.CaptionHeight + NonClientArea.SizingFrameWidth;

    public static void Arrange(MdiClient client, MdiLayout layout, IReadOnlyList<Form> children)
    {
        if (layout == MdiLayout.ArrangeIcons)
        {
            ArrangeMinimized(client, children);
            return;
        }

        var windows = children.Where(child => child.WindowState != FormWindowState.Minimized).ToList();
        if (windows.Count == 0) return;

        foreach (var window in windows)
        {
            if (window.WindowState == FormWindowState.Maximized) window.WindowState = FormWindowState.Normal;
        }

        var area = client.ClientRectangle;
        if (layout == MdiLayout.Cascade) Cascade(area, windows);
        else Tile(area, windows, stackedVertically: layout == MdiLayout.TileHorizontal);
    }

    /// <summary>
    /// Each window a step below and right of the previous one, the active window last so it ends
    /// on top, all the same size: three quarters of the area, as an MDI frame does by default.
    /// </summary>
    private static void Cascade(Rectangle area, List<Form> windows)
    {
        var active = windows.FirstOrDefault(window => window.IsActiveChild);
        if (active is not null)
        {
            windows.Remove(active);
            windows.Add(active);
        }

        var size = new Size(Math.Max(area.Width * 3 / 4, 1), Math.Max(area.Height * 3 / 4, 1));

        for (var index = 0; index < windows.Count; index++)
        {
            // Starts over at the top left once a window would leave the area.
            var steps = index % Math.Max((area.Height - size.Height) / CascadeStep + 1, 1);
            windows[index].Bounds = new Rectangle(area.Left + steps * CascadeStep, area.Top + steps * CascadeStep, size.Width, size.Height);
            windows[index].BringToFront();
        }
    }

    /// <summary>
    /// TileHorizontal stacks the windows one above another, each the full width; TileVertical
    /// puts them side by side, each the full height. The last one takes what division leaves.
    /// </summary>
    private static void Tile(Rectangle area, List<Form> windows, bool stackedVertically)
    {
        var count = windows.Count;
        var length = (stackedVertically ? area.Height : area.Width) / count;

        for (var index = 0; index < count; index++)
        {
            var start = index * length;
            var end = index == count - 1 ? (stackedVertically ? area.Height : area.Width) : start + length;

            windows[index].Bounds = stackedVertically
                ? new Rectangle(area.Left, area.Top + start, area.Width, end - start)
                : new Rectangle(area.Left + start, area.Top, end - start, area.Height);
        }
    }

    /// <summary>The minimized children side by side along the bottom (or the top), wrapping onto a new row.</summary>
    private static void ArrangeMinimized(MdiClient client, IReadOnlyList<Form> children)
    {
        var area = client.ClientRectangle;
        var anchorBottom = client.Parent is not Form { MdiChildrenMinimizedAnchorBottom: false };
        var x = area.Left;
        var row = 0;

        foreach (var child in children.Where(child => child.WindowState == FormWindowState.Minimized))
        {
            var size = child.Size;
            if (x > area.Left && x + size.Width > area.Right)
            {
                x = area.Left;
                row++;
            }

            var y = anchorBottom ? area.Bottom - (row + 1) * size.Height : area.Top + row * size.Height;
            child.Location = new Point(x, y);
            x += size.Width;
        }
    }
}
