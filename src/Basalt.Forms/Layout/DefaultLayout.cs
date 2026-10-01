using System.Drawing;

namespace System.Windows.Forms.Layout;

/// <summary>
/// The layout of every container that does not choose another: docking,
/// then anchoring, then growing auto-sized children.
/// </summary>
/// <remarks>
/// Written from WinForms' documented behaviour, the classic anchor layout
/// that is still the default (the "AnchorLayoutV2" switch is off unless an
/// application turns it on):
/// <list type="bullet">
/// <item>Docked children are placed in reverse z-order: the child at the back
/// (last index) takes its edge first, the one in front is placed last. A
/// Fill child takes what is left without consuming it.</item>
/// <item>Anchored children keep the distances measured when their bounds
/// were last set from outside a layout, so a child anchored right stays the
/// same distance from the right edge when the container widens.</item>
/// <item>Auto-sized children grow to their preferred size, towards the left
/// or upward when anchored only to the right or bottom.</item>
/// </list>
/// The layout passes <see cref="BoundsSpecified.None"/> when it moves a
/// child, which is what keeps the measured distances from being remeasured.
/// </remarks>
internal sealed class DefaultLayout : LayoutEngine
{
    public static DefaultLayout Instance { get; } = new();

    public override void InitLayout(object child, BoundsSpecified specified)
    {
        if (child is IArrangedElement element) InitLayout(element, specified);
    }

    public override bool Layout(object container, LayoutEventArgs layoutEventArgs) =>
        container is IArrangedElement element && Layout(element);

    internal static void InitLayout(IArrangedElement element, BoundsSpecified specified)
    {
        if (specified != BoundsSpecified.None) MeasureAnchors(element);
    }

    /// <summary>Arranges the children; true when the container sizes itself, so its parent must lay out again.</summary>
    internal static bool Layout(IArrangedElement container)
    {
        var children = container.Children;
        if (children.Count == 0) return container.LayoutStore.AutoSize;

        LayoutDocked(container);

        var pending = new Dictionary<IArrangedElement, Rectangle>();
        LayoutAnchored(container, pending);
        GrowAutoSized(container, pending);

        foreach (var (element, bounds) in pending)
        {
            if (element.Bounds != bounds) element.SetBounds(bounds, BoundsSpecified.None);
        }

        return container.LayoutStore.AutoSize;
    }

    /// <summary>
    /// Records how far an anchored element is from its container's edges, at
    /// the container's present size. Called whenever the bounds or the anchor
    /// are set from outside a layout.
    /// </summary>
    internal static void MeasureAnchors(IArrangedElement element)
    {
        var store = element.LayoutStore;
        if (element.Container is not { } container || store.Dock != DockStyle.None)
        {
            store.AnchorInfo = null;
            return;
        }

        var display = container.DisplayRectangle;
        var bounds = element.Bounds;
        var anchor = store.Anchor;

        var distances = new AnchorDistances
        {
            Left = bounds.Left - display.X,
            Top = bounds.Top - display.Y,
            Right = bounds.Right - display.X,
            Bottom = bounds.Bottom - display.Y,
        };

        if (anchor.HasFlag(AnchorStyles.Right))
        {
            distances.Right -= display.Width;
            if (!anchor.HasFlag(AnchorStyles.Left)) distances.Left -= display.Width;
        }
        else if (!anchor.HasFlag(AnchorStyles.Left))
        {
            distances.Right -= display.Width / 2;
            distances.Left -= display.Width / 2;
        }

        if (anchor.HasFlag(AnchorStyles.Bottom))
        {
            distances.Bottom -= display.Height;
            if (!anchor.HasFlag(AnchorStyles.Top)) distances.Top -= display.Height;
        }
        else if (!anchor.HasFlag(AnchorStyles.Top))
        {
            distances.Bottom -= display.Height / 2;
            distances.Top -= display.Height / 2;
        }

        store.AnchorInfo = distances;
    }

    /// <summary>Where an anchored element goes in a display rectangle of the given size.</summary>
    internal static Rectangle AnchoredBounds(IArrangedElement element, Rectangle display)
    {
        var store = element.LayoutStore;
        if (store.AnchorInfo is null) MeasureAnchors(element);

        var distances = store.AnchorInfo!;
        var anchor = store.Anchor;

        var left = distances.Left + display.X;
        var top = distances.Top + display.Y;
        var right = distances.Right + display.X;
        var bottom = distances.Bottom + display.Y;

        if (anchor.HasFlag(AnchorStyles.Right))
        {
            right += display.Width;
            if (!anchor.HasFlag(AnchorStyles.Left)) left += display.Width;
        }
        else if (!anchor.HasFlag(AnchorStyles.Left))
        {
            right += display.Width / 2;
            left += display.Width / 2;
        }

        if (anchor.HasFlag(AnchorStyles.Bottom))
        {
            bottom += display.Height;
            if (!anchor.HasFlag(AnchorStyles.Top)) top += display.Height;
        }
        else if (!anchor.HasFlag(AnchorStyles.Top))
        {
            bottom += display.Height / 2;
            top += display.Height / 2;
        }

        // A container narrower than the distances collapses the element, never inverts it.
        right = Math.Max(right, left);
        bottom = Math.Max(bottom, top);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static void LayoutDocked(IArrangedElement container)
    {
        var remaining = container.DisplayRectangle;
        var children = container.Children;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var element = children.ElementAt(i);
            var store = element.LayoutStore;
            if (!store.NeedsDockLayout(element)) continue;

            switch (store.Dock)
            {
                case DockStyle.Top:
                {
                    var height = DockedHeight(element, remaining.Size);
                    element.SetBounds(new Rectangle(remaining.X, remaining.Y, remaining.Width, height), BoundsSpecified.None);

                    // The element may have refused the size: what it took is what it has.
                    remaining.Y += element.Bounds.Height;
                    remaining.Height -= element.Bounds.Height;
                    break;
                }

                case DockStyle.Bottom:
                {
                    var height = DockedHeight(element, remaining.Size);
                    element.SetBounds(new Rectangle(remaining.X, remaining.Bottom - height, remaining.Width, height), BoundsSpecified.None);
                    remaining.Height -= element.Bounds.Height;
                    break;
                }

                case DockStyle.Left:
                {
                    var width = DockedWidth(element, remaining.Size);
                    element.SetBounds(new Rectangle(remaining.X, remaining.Y, width, remaining.Height), BoundsSpecified.None);
                    remaining.X += element.Bounds.Width;
                    remaining.Width -= element.Bounds.Width;
                    break;
                }

                case DockStyle.Right:
                {
                    var width = DockedWidth(element, remaining.Size);
                    element.SetBounds(new Rectangle(remaining.Right - width, remaining.Y, width, remaining.Height), BoundsSpecified.None);
                    remaining.Width -= element.Bounds.Width;
                    break;
                }

                case DockStyle.Fill:
                    element.SetBounds(remaining, BoundsSpecified.None);
                    break;
            }

            // Overlapping docked edges leave nothing, not a negative area.
            remaining.Width = Math.Max(remaining.Width, 0);
            remaining.Height = Math.Max(remaining.Height, 0);
        }
    }

    /// <summary>The height a top or bottom docked element takes: its own, or its preferred one when it sizes itself.</summary>
    private static int DockedHeight(IArrangedElement element, Size remaining)
    {
        if (!element.LayoutStore.AutoSize) return element.Bounds.Height;

        return element.GetPreferredSize(new Size(remaining.Width, 0)).Height;
    }

    private static int DockedWidth(IArrangedElement element, Size remaining)
    {
        if (!element.LayoutStore.AutoSize) return element.Bounds.Width;

        return element.GetPreferredSize(new Size(0, remaining.Height)).Width;
    }

    private static void LayoutAnchored(IArrangedElement container, Dictionary<IArrangedElement, Rectangle> pending)
    {
        var display = container.DisplayRectangle;

        // A container that sizes itself and has not been given a size yet would squash every child to nothing.
        if (container.LayoutStore.AutoSize && (display.Width == 0 || display.Height == 0)) return;

        var children = container.Children;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var element = children.ElementAt(i);
            if (!element.LayoutStore.NeedsAnchorLayout(element)) continue;

            pending[element] = AnchoredBounds(element, display);
        }
    }

    private static void GrowAutoSized(IArrangedElement container, Dictionary<IArrangedElement, Rectangle> pending)
    {
        var children = container.Children;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var element = children.ElementAt(i);
            var store = element.LayoutStore;
            if (!store.AutoSize || store.Dock != DockStyle.None || !element.ParticipatesInLayout) continue;

            var bounds = pending.TryGetValue(element, out var anchored) ? anchored : element.Bounds;
            var anchor = store.Anchor;

            // A side anchored at both ends is fixed by the container; only the other one is free.
            var constraint = new Size(int.MaxValue, int.MaxValue);
            if ((anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right)) constraint.Width = bounds.Width;
            if ((anchor & (AnchorStyles.Top | AnchorStyles.Bottom)) == (AnchorStyles.Top | AnchorStyles.Bottom)) constraint.Height = bounds.Height;

            var preferred = element.GetPreferredSize(constraint);
            var size = store.AutoSizeMode == AutoSizeMode.GrowAndShrink
                ? preferred
                : new Size(Math.Max(bounds.Width, preferred.Width), Math.Max(bounds.Height, preferred.Height));

            if (size == bounds.Size)
            {
                if (pending.ContainsKey(element)) pending[element] = bounds;
                continue;
            }

            pending[element] = Grow(bounds, size, anchor);
        }
    }

    /// <summary>Resizes, keeping the anchored edge still: an element anchored only right grows to the left.</summary>
    private static Rectangle Grow(Rectangle bounds, Size size, AnchorStyles anchor)
    {
        var location = bounds.Location;

        if (anchor.HasFlag(AnchorStyles.Right) && !anchor.HasFlag(AnchorStyles.Left)) location.X -= size.Width - bounds.Width;
        if (anchor.HasFlag(AnchorStyles.Bottom) && !anchor.HasFlag(AnchorStyles.Top)) location.Y -= size.Height - bounds.Height;

        return new Rectangle(location, size);
    }

    /// <summary>
    /// The size a container laid out by this engine needs to show all its
    /// children: docked ones stacked, the others where they are, plus the
    /// container's padding.
    /// </summary>
    internal static Size PreferredSize(IArrangedElement container, Size proposed)
    {
        var children = container.Children;
        var docked = Size.Empty;
        var extent = Size.Empty;

        // Docked children measured from the inside out, as they are placed.
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var element = children.ElementAt(i);
            var store = element.LayoutStore;
            if (!element.ParticipatesInLayout) continue;

            if (store.Dock == DockStyle.None)
            {
                var bounds = element.Bounds;
                var margin = store.Margin;
                var display = container.DisplayRectangle;
                extent.Width = Math.Max(extent.Width, bounds.Right + margin.Right - display.X);
                extent.Height = Math.Max(extent.Height, bounds.Bottom + margin.Bottom - display.Y);
                continue;
            }

            var size = store.AutoSize ? element.GetPreferredSize(Size.Empty) : element.Bounds.Size;

            switch (store.Dock)
            {
                case DockStyle.Top or DockStyle.Bottom:
                    docked.Height += size.Height;
                    docked.Width = Math.Max(docked.Width, size.Width);
                    break;

                case DockStyle.Left or DockStyle.Right:
                    docked.Width += size.Width;
                    docked.Height = Math.Max(docked.Height, size.Height);
                    break;

                case DockStyle.Fill:
                    docked.Width = Math.Max(docked.Width, size.Width);
                    docked.Height = Math.Max(docked.Height, size.Height);
                    break;
            }
        }

        var padding = container.LayoutStore.Padding;

        return new Size(
            Math.Max(docked.Width, extent.Width) + padding.Horizontal,
            Math.Max(docked.Height, extent.Height) + padding.Vertical);
    }
}
