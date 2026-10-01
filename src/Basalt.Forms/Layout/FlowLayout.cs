using System.Drawing;

namespace System.Windows.Forms.Layout;

/// <summary>
/// Arranges children one after another along a direction, wrapping into a
/// new row or column when one would not fit and the container allows
/// wrapping.
/// </summary>
/// <remarks>
/// Written from WinForms' documented behaviour of FlowLayoutPanel:
/// <list type="bullet">
/// <item>Children are flowed in the order they appear in the container's
/// collection (the order they were added), not reversed as docking is.</item>
/// <item>A child's <see cref="Control.Margin"/> adds space before and after
/// it; margins are not collapsed between neighbours.</item>
/// <item>A child whose flow break is set (<see cref="SetFlowBreak"/>) ends
/// its row or column even when more would still fit.</item>
/// <item>A child anchored to both sides of the row's cross axis (top and
/// bottom when flowing horizontally, left and right when flowing
/// vertically) is stretched to fill it.</item>
/// <item><see cref="FlowDirection.RightToLeft"/> and
/// <see cref="FlowDirection.BottomUp"/> fill from the far edge of their own
/// axis; a container whose <see cref="Control.RightToLeft"/> is
/// <see cref="System.Windows.Forms.RightToLeft.Yes"/> mirrors the X axis on
/// top of that, as WinForms documents for a right-to-left flow panel.</item>
/// </list>
/// This engine reads and writes only <see cref="IArrangedElement"/>, so a
/// future ToolStrip can share it for its items without depending on
/// FlowLayoutPanel.
/// </remarks>
internal sealed class FlowLayout : LayoutEngine
{
    public static FlowLayout Instance { get; } = new();

    private static readonly object FlowDirectionKey = new();
    private static readonly object WrapContentsKey = new();
    private static readonly object FlowBreakKey = new();

    internal static FlowDirection GetFlowDirection(IArrangedElement container) =>
        container.LayoutStore.Extended.TryGetValue(FlowDirectionKey, out var value) && value is FlowDirection direction
            ? direction
            : FlowDirection.LeftToRight;

    internal static void SetFlowDirection(IArrangedElement container, FlowDirection value) =>
        container.LayoutStore.Extended[FlowDirectionKey] = value;

    internal static bool GetWrapContents(IArrangedElement container) =>
        !container.LayoutStore.Extended.TryGetValue(WrapContentsKey, out var value) || (bool)value!;

    internal static void SetWrapContents(IArrangedElement container, bool value) =>
        container.LayoutStore.Extended[WrapContentsKey] = value;

    internal static bool GetFlowBreak(IArrangedElement element) =>
        element.LayoutStore.Extended.TryGetValue(FlowBreakKey, out var value) && (bool)value!;

    internal static void SetFlowBreak(IArrangedElement element, bool value)
    {
        if (value) element.LayoutStore.Extended[FlowBreakKey] = true;
        else element.LayoutStore.Extended.Remove(FlowBreakKey);
    }

    public override bool Layout(object container, LayoutEventArgs layoutEventArgs) =>
        container is IArrangedElement element && Layout(element);

    /// <summary>Arranges the children; true when the container sizes itself, so its parent must lay out again.</summary>
    internal static bool Layout(IArrangedElement container)
    {
        var children = FlowChildren(container);
        if (children.Count == 0) return container.LayoutStore.AutoSize;

        var direction = GetFlowDirection(container);
        var horizontal = IsHorizontal(direction);
        var wrap = GetWrapContents(container);
        var display = container.DisplayRectangle;
        var primaryLimit = horizontal ? display.Width : display.Height;

        var rows = BuildRows(children, horizontal, primaryLimit, wrap);
        AssignLocalBounds(rows, horizontal);

        var mirrorPrimary = horizontal ? direction == FlowDirection.RightToLeft : direction == FlowDirection.BottomUp;
        var mirrorX = container is Control { RightToLeft: RightToLeft.Yes };

        foreach (var row in rows)
        {
            foreach (var item in row.Items)
            {
                var rect = item.Bounds;

                if (mirrorPrimary)
                {
                    if (horizontal) rect.X = display.Width - rect.Right;
                    else rect.Y = display.Height - rect.Bottom;
                }

                if (mirrorX) rect.X = display.Width - rect.Right;

                rect.Offset(display.X, display.Y);

                if (item.Element.Bounds != rect) item.Element.SetBounds(rect, BoundsSpecified.None);
            }
        }

        return container.LayoutStore.AutoSize;
    }

    /// <summary>The size the container needs to flow every child without clipping any of them.</summary>
    internal static Size PreferredSize(IArrangedElement container, Size proposedSize)
    {
        var padding = container.LayoutStore.Padding;
        var children = FlowChildren(container);
        if (children.Count == 0) return new Size(padding.Horizontal, padding.Vertical);

        var horizontal = IsHorizontal(GetFlowDirection(container));
        var wrap = GetWrapContents(container);

        var proposedPrimary = horizontal ? proposedSize.Width - padding.Horizontal : proposedSize.Height - padding.Vertical;
        var primaryLimit = wrap && proposedPrimary is > 0 and < int.MaxValue ? proposedPrimary : int.MaxValue;

        var rows = BuildRows(children, horizontal, primaryLimit, wrap);

        var primaryTotal = 0;
        var crossTotal = 0;

        foreach (var row in rows)
        {
            primaryTotal = Math.Max(primaryTotal, row.Primary);
            crossTotal += row.Cross;
        }

        return horizontal
            ? new Size(primaryTotal + padding.Horizontal, crossTotal + padding.Vertical)
            : new Size(crossTotal + padding.Horizontal, primaryTotal + padding.Vertical);
    }

    private static bool IsHorizontal(FlowDirection direction) => direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;

    /// <summary>The children that flow: visible (or kept for layout) and not docked, in the order they were added.</summary>
    private static List<IArrangedElement> FlowChildren(IArrangedElement container)
    {
        var children = container.Children;
        var result = new List<IArrangedElement>(children.Count);

        for (var i = 0; i < children.Count; i++)
        {
            var element = children.ElementAt(i);
            if (element.ParticipatesInLayout && element.LayoutStore.Dock == DockStyle.None) result.Add(element);
        }

        return result;
    }

    private static Size NaturalSize(IArrangedElement element) =>
        element.LayoutStore.AutoSize ? element.GetPreferredSize(Size.Empty) : element.Bounds.Size;

    /// <summary>Packs the children into rows (or columns), wrapping on the primary axis and on a flow break.</summary>
    private static List<Row> BuildRows(List<IArrangedElement> children, bool horizontal, int primaryLimit, bool wrap)
    {
        var rows = new List<Row>();
        var current = new Row();
        var cursor = 0;

        foreach (var element in children)
        {
            var margin = element.LayoutStore.Margin;
            var size = NaturalSize(element);
            var primarySize = horizontal ? margin.Left + size.Width + margin.Right : margin.Top + size.Height + margin.Bottom;

            if (wrap && current.Items.Count > 0 && cursor + primarySize > primaryLimit)
            {
                FinishRow(current, horizontal);
                rows.Add(current);
                current = new Row();
                cursor = 0;
            }

            current.Items.Add(new FlowItem(element, size, margin));
            cursor += primarySize;

            if (GetFlowBreak(element))
            {
                FinishRow(current, horizontal);
                rows.Add(current);
                current = new Row();
                cursor = 0;
            }
        }

        if (current.Items.Count > 0)
        {
            FinishRow(current, horizontal);
            rows.Add(current);
        }

        return rows;
    }

    /// <summary>Measures a finished row: how much of the primary axis it used, and the tallest (or widest) cross size in it.</summary>
    private static void FinishRow(Row row, bool horizontal)
    {
        var primary = 0;
        var cross = 0;

        foreach (var item in row.Items)
        {
            primary += horizontal
                ? item.Margin.Left + item.Size.Width + item.Margin.Right
                : item.Margin.Top + item.Size.Height + item.Margin.Bottom;

            var itemCross = horizontal
                ? item.Margin.Top + item.Size.Height + item.Margin.Bottom
                : item.Margin.Left + item.Size.Width + item.Margin.Right;

            cross = Math.Max(cross, itemCross);
        }

        row.Primary = primary;
        row.Cross = cross;
    }

    /// <summary>
    /// Places every item within its row in local, unmirrored coordinates
    /// starting at (0, 0): rows (or columns) stacked one after another, items
    /// top- or left-aligned within their row unless stretched to fill it.
    /// </summary>
    private static void AssignLocalBounds(List<Row> rows, bool horizontal)
    {
        var crossCursor = 0;

        foreach (var row in rows)
        {
            var primaryCursor = 0;

            foreach (var item in row.Items)
            {
                var margin = item.Margin;
                var primaryStart = primaryCursor + (horizontal ? margin.Left : margin.Top);
                var crossStart = crossCursor + (horizontal ? margin.Top : margin.Left);

                var stretch = horizontal
                    ? (item.Element.LayoutStore.Anchor & (AnchorStyles.Top | AnchorStyles.Bottom)) == (AnchorStyles.Top | AnchorStyles.Bottom)
                    : (item.Element.LayoutStore.Anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right);

                var size = item.Size;

                if (stretch)
                {
                    var crossMarginSum = horizontal ? margin.Top + margin.Bottom : margin.Left + margin.Right;
                    var stretched = Math.Max(row.Cross - crossMarginSum, 0);
                    size = horizontal ? new Size(size.Width, stretched) : new Size(stretched, size.Height);
                }

                item.Bounds = horizontal
                    ? new Rectangle(primaryStart, crossStart, size.Width, size.Height)
                    : new Rectangle(crossStart, primaryStart, size.Width, size.Height);

                primaryCursor += horizontal
                    ? margin.Left + item.Size.Width + margin.Right
                    : margin.Top + item.Size.Height + margin.Bottom;
            }

            crossCursor += row.Cross;
        }
    }

    private sealed class FlowItem(IArrangedElement element, Size size, Padding margin)
    {
        public IArrangedElement Element { get; } = element;

        public Size Size { get; } = size;

        public Padding Margin { get; } = margin;

        public Rectangle Bounds { get; set; }
    }

    private sealed class Row
    {
        public List<FlowItem> Items { get; } = [];

        /// <summary>How much of the primary axis (width flowing horizontally, height flowing vertically) this row used.</summary>
        public int Primary { get; set; }

        /// <summary>The row's size on the cross axis: its height flowing horizontally, its width flowing vertically.</summary>
        public int Cross { get; set; }
    }
}
