using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>
/// The scenarios compared between real Windows Forms and Basalt.Forms.
/// </summary>
/// <remarks>
/// This one file is compiled twice: into the generator, against Microsoft's
/// System.Windows.Forms (brought by UseWindowsForms), and into the tests,
/// against ours (the System.Windows.Forms assembly built from
/// src/Basalt.Forms). The two APIs are the same, so the same lines build and
/// drive the same controls on both sides, and only the recorded behaviour can
/// differ: the order events fire in, where layout places a child, how a
/// property's quirks round-trip. Every scenario uses only the Control,
/// ScrollableControl and Form surface our implementation already has: no
/// Button, Panel or other concrete control is needed to exercise layout,
/// visibility, colour inheritance and the window-style frame.
/// </remarks>
public static class Scenarios
{
    public sealed record Scenario(string Name, Func<IReadOnlyList<string>> Run);

    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("dock-order-follows-z-order-and-bring-to-front", DockOrderFollowsZOrderAndBringToFront),
        new("dock-fill-does-not-consume-space", DockFillDoesNotConsumeSpace),
        new("dock-undock-restores-specified-bounds", DockUndockRestoresSpecifiedBounds),
        new("anchor-right-moves-without-stretching", AnchorRightMovesWithoutStretching),
        new("anchor-bottom-moves-without-stretching", AnchorBottomMovesWithoutStretching),
        new("anchor-all-sides-stretches", AnchorAllSidesStretches),
        new("anchor-none-centers", AnchorNoneCenters),
        new("anchor-shrink-past-stretched-child-clamps", AnchorShrinkPastStretchedChildClamps),
        new("anchor-set-while-suspended-designer-pattern", AnchorSetWhileSuspendedDesignerPattern),
        new("deferred-layout-reports-first-cause", DeferredLayoutReportsFirstCause),
        new("event-order-on-bounds-change", EventOrderOnBoundsChange),
        new("visible-propagation-and-change-counts", VisiblePropagationAndChangeCounts),
        new("enabled-propagation-and-change-counts", EnabledPropagationAndChangeCounts),
        new("backcolor-inherits-until-set-and-change-counts", BackColorInheritsUntilSetAndChangeCounts),
        new("client-size-with-no-frame", ClientSizeWithNoFrame),
        new("client-size-with-border", ClientSizeWithBorder),
        new("client-size-with-border-edge-and-vscroll", ClientSizeWithBorderEdgeAndVScroll),
        new("anchor-set-while-docked-undocks-with-new-anchor", AnchorSetWhileDockedUndocksWithNewAnchor),
        new("dock-resets-anchor-and-undock-does-not-restore-it", DockResetsAnchorAndUndockDoesNotRestoreIt),
        new("padding-on-scrollable-control-shrinks-display-rectangle", PaddingOnScrollableControlShrinksDisplayRectangle),
        new("minimum-size-raises-size-and-maximum", MinimumSizeRaisesSizeAndMaximum),
        new("maximum-size-lowers-size-and-minimum", MaximumSizeLowersSizeAndMinimum),
        new("padding-all-reports-minus-one-after-one-side-set", PaddingAllReportsMinusOneAfterOneSideSet),
        new("padding-clamps-negative-sides", PaddingClampsNegativeSides),
        new("autosize-control-preferred-size-is-specified-not-current-bounds", AutoSizeControlPreferredSizeIsSpecifiedNotCurrentBounds),
        new("control-added-and-removed-events", ControlAddedAndRemovedEvents),
        new("set-child-index-changes-dock-order", SetChildIndexChangesDockOrder),
        new("parent-changed-event-on-reparenting", ParentChangedEventOnReparenting),
        new("form-starts-hidden-and-cascades-visibility-to-children", FormStartsHiddenAndCascadesVisibilityToChildren),
    ];

    // Logging helpers: each returns a delegate that appends one formatted line, so a scenario reads as which
    // events it watches and which actions it performs, not as how a list is built.

    private static EventHandler Logger(List<string> log, string message) => (_, _) => log.Add(message);

    private static LayoutEventHandler LayoutLogger(List<string> log, string name) =>
        (_, e) => log.Add($"{name}.Layout(AffectedProperty={e.AffectedProperty}, AffectedControl={e.AffectedControl?.Name})");

    private static ControlEventHandler ControlLogger(List<string> log, string name, string verb) =>
        (_, e) => log.Add($"{name}.{verb}({e.Control?.Name})");

    /// <summary>A control whose window styles grow by the given bits, to read how they change ClientSize.</summary>
    private sealed class StyledControl : Control
    {
        private readonly int _extraStyle;
        private readonly int _extraExStyle;

        public StyledControl(int extraStyle, int extraExStyle)
        {
            _extraStyle = extraStyle;
            _extraExStyle = extraExStyle;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.Style |= _extraStyle;
                parameters.ExStyle |= _extraExStyle;
                return parameters;
            }
        }
    }

    private const int WsBorder = 0x00800000;
    private const int WsVScroll = 0x00200000;
    private const int WsExClientEdge = 0x200;

    // Dock order.

    private static IReadOnlyList<string> DockOrderFollowsZOrderAndBringToFront()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 60) };
        var first = new Control { Name = "first", Dock = DockStyle.Top, Height = 10 };
        var second = new Control { Name = "second", Dock = DockStyle.Top, Height = 10 };

        // Added in this order: first is in front (index 0), second at the back (index 1).
        // Docking is placed in reverse z-order, so the back one, second, claims the top strip first.
        parent.Controls.Add(first);
        parent.Controls.Add(second);
        parent.PerformLayout();
        log.Add($"before.first.Bounds={first.Bounds}");
        log.Add($"before.second.Bounds={second.Bounds}");

        // Bringing second to the front swaps who is now at the back, and so who docks first.
        second.BringToFront();
        parent.PerformLayout();
        log.Add($"after.first.Bounds={first.Bounds}");
        log.Add($"after.second.Bounds={second.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> DockFillDoesNotConsumeSpace()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 80) };
        var top = new Control { Name = "top", Dock = DockStyle.Top, Height = 10 };
        var fill = new Control { Name = "fill", Dock = DockStyle.Fill };

        // top is added first (front, index 0), fill second (back, index 1): fill is docked first,
        // and a Fill child never shrinks the remaining area for the docks processed after it, so
        // top still claims the full width at the top, overlapping the area fill already took.
        parent.Controls.Add(top);
        parent.Controls.Add(fill);
        parent.PerformLayout();

        log.Add($"fill.Bounds={fill.Bounds}");
        log.Add($"top.Bounds={top.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> DockUndockRestoresSpecifiedBounds()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(5, 5, 20, 20) };
        parent.Controls.Add(child);

        child.Dock = DockStyle.Fill;
        parent.PerformLayout();
        log.Add($"docked.child.Bounds={child.Bounds}");

        child.Dock = DockStyle.None;
        log.Add($"undocked.child.Bounds={child.Bounds}");

        return log;
    }

    // Anchoring.

    private static IReadOnlyList<string> AnchorRightMovesWithoutStretching()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(70, 10, 20, 20) };
        parent.Controls.Add(child);

        // Anchored to the top and the right, but not the left: the child keeps its distance from
        // the right edge and moves with it, keeping its size.
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        parent.Width = 150;
        log.Add($"child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> AnchorBottomMovesWithoutStretching()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(10, 70, 20, 20) };
        parent.Controls.Add(child);

        child.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        parent.Height = 150;
        log.Add($"child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> AnchorAllSidesStretches()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(10, 10, 80, 80) };
        parent.Controls.Add(child);

        // Anchored on every side: the margins (10 on each side) stay fixed, so the child's size
        // changes with the container instead of its position.
        child.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        parent.Size = new Size(140, 120);
        log.Add($"child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> AnchorNoneCenters()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(40, 40, 20, 20) };
        parent.Controls.Add(child);

        // Anchored on no side: both edges are measured from the middle, so the child floats,
        // keeping itself centred as the container grows.
        child.Anchor = AnchorStyles.None;
        parent.Size = new Size(140, 140);
        log.Add($"child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> AnchorShrinkPastStretchedChildClamps()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(200, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(20, 10, 160, 80) };
        parent.Controls.Add(child);

        child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // The margins (20 on the left, 20 on the right) no longer fit in a container this narrow:
        // the computed width would be negative, and the layout collapses it to zero instead of
        // inverting the rectangle.
        parent.Width = 30;
        log.Add($"child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> AnchorSetWhileSuspendedDesignerPattern()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child" };
        parent.Controls.Add(child);

        // The pattern a designer-generated InitializeComponent writes: everything set while the
        // container is suspended, then ResumeLayout(false), which only re-measures anchors from
        // the bounds the children have now, without laying out.
        parent.SuspendLayout();
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        child.Bounds = new Rectangle(70, 10, 20, 20);
        parent.ResumeLayout(false);
        parent.PerformLayout();
        log.Add($"after-resume.child.Bounds={child.Bounds}");

        // Growing the parent now moves the child by the distance measured at ResumeLayout time
        // (10 pixels from the right), not from where it was before the designer's code ran.
        parent.Width = 140;
        log.Add($"after-grow.child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> DeferredLayoutReportsFirstCause()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var a = new Control { Name = "a", Bounds = new Rectangle(0, 0, 20, 20) };
        var b = new Control { Name = "b", Bounds = new Rectangle(0, 0, 20, 20) };
        parent.Controls.Add(a);
        parent.Controls.Add(b);
        parent.Layout += LayoutLogger(log, "parent");

        parent.SuspendLayout();
        a.Width = 30;
        b.Height = 40;
        parent.ResumeLayout(true);

        return log;
    }

    private static IReadOnlyList<string> EventOrderOnBoundsChange()
    {
        var log = new List<string>();
        var control = new Control { Name = "c", Bounds = new Rectangle(0, 0, 50, 50) };

        control.Move += Logger(log, "c.Move");
        control.LocationChanged += Logger(log, "c.LocationChanged");
        control.Resize += Logger(log, "c.Resize");
        control.SizeChanged += Logger(log, "c.SizeChanged");
        control.ClientSizeChanged += Logger(log, "c.ClientSizeChanged");

        control.Bounds = new Rectangle(10, 20, 80, 90);

        return log;
    }

    // Visible, Enabled and BackColor propagation.

    private static IReadOnlyList<string> VisiblePropagationAndChangeCounts()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent" };
        var child = new Control { Name = "child" };
        var grandchild = new Control { Name = "grandchild" };
        parent.Controls.Add(child);
        child.Controls.Add(grandchild);

        var childChanges = 0;
        var grandchildChanges = 0;
        child.VisibleChanged += (_, _) => childChanges++;
        grandchild.VisibleChanged += (_, _) => grandchildChanges++;

        parent.Visible = false;
        log.Add($"after-hide-parent.child.Visible={child.Visible}");
        log.Add($"after-hide-parent.grandchild.Visible={grandchild.Visible}");

        parent.Visible = true;
        log.Add($"after-show-parent.child.Visible={child.Visible}");
        log.Add($"after-show-parent.grandchild.Visible={grandchild.Visible}");

        // Hiding the child directly, with the parent visible, still cascades to the grandchild.
        child.Visible = false;
        log.Add($"after-hide-child.grandchild.Visible={grandchild.Visible}");

        log.Add($"child.VisibleChanged.count={childChanges}");
        log.Add($"grandchild.VisibleChanged.count={grandchildChanges}");

        return log;
    }

    private static IReadOnlyList<string> EnabledPropagationAndChangeCounts()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent" };
        var child = new Control { Name = "child" };
        var disabledChild = new Control { Name = "disabledChild" };
        parent.Controls.Add(child);
        parent.Controls.Add(disabledChild);
        disabledChild.Enabled = false;

        var childChanges = 0;
        var disabledChildChanges = 0;
        child.EnabledChanged += (_, _) => childChanges++;
        disabledChild.EnabledChanged += (_, _) => disabledChildChanges++;

        parent.Enabled = false;
        log.Add($"after-disable-parent.child.Enabled={child.Enabled}");
        log.Add($"after-disable-parent.disabledChild.Enabled={disabledChild.Enabled}");

        parent.Enabled = true;
        log.Add($"after-enable-parent.child.Enabled={child.Enabled}");
        log.Add($"after-enable-parent.disabledChild.Enabled={disabledChild.Enabled}");

        log.Add($"child.EnabledChanged.count={childChanges}");

        // Already disabled on its own, so the parent's own toggling never flips it: no event fires.
        log.Add($"disabledChild.EnabledChanged.count={disabledChildChanges}");

        return log;
    }

    private static IReadOnlyList<string> BackColorInheritsUntilSetAndChangeCounts()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent" };
        var child = new Control { Name = "child" };
        parent.Controls.Add(child);

        var childChanges = 0;
        child.BackColorChanged += (_, _) => childChanges++;

        parent.BackColor = Color.Red;
        log.Add($"after-first-parent-color.child.BackColor={child.BackColor}");
        log.Add($"after-first-parent-color.child.BackColorChanged.count={childChanges}");

        child.BackColor = Color.Blue;
        log.Add($"after-own-color.child.BackColor={child.BackColor}");

        // The child now has its own colour: the parent's colour no longer reaches it.
        parent.BackColor = Color.Green;
        log.Add($"after-second-parent-color.child.BackColor={child.BackColor}");
        log.Add($"after-second-parent-color.child.BackColorChanged.count={childChanges}");

        return log;
    }

    // ClientSize for the window styles a CreateParams override can add.

    private static IReadOnlyList<string> ClientSizeWithNoFrame()
    {
        var control = new StyledControl(0, 0) { Size = new Size(100, 50) };

        return [$"control.ClientSize={control.ClientSize}"];
    }

    private static IReadOnlyList<string> ClientSizeWithBorder()
    {
        var control = new StyledControl(WsBorder, 0) { Size = new Size(100, 50) };

        return [$"control.ClientSize={control.ClientSize}"];
    }

    private static IReadOnlyList<string> ClientSizeWithBorderEdgeAndVScroll()
    {
        var control = new StyledControl(WsBorder | WsVScroll, WsExClientEdge) { Size = new Size(100, 50) };

        return [$"control.ClientSize={control.ClientSize}"];
    }

    // Dock and Anchor interplay.

    private static IReadOnlyList<string> AnchorSetWhileDockedUndocksWithNewAnchor()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(0, 0, 100, 10) };
        parent.Controls.Add(child);

        child.Dock = DockStyle.Top;
        log.Add($"after-dock.Dock={child.Dock}");
        log.Add($"after-dock.Anchor={child.Anchor}");

        // Setting an anchor while docked undocks first (the last of the two set wins), then
        // applies the requested anchor, and the bounds fall back to the ones set before docking.
        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        log.Add($"after-anchor.Dock={child.Dock}");
        log.Add($"after-anchor.Anchor={child.Anchor}");
        log.Add($"after-anchor.child.Bounds={child.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> DockResetsAnchorAndUndockDoesNotRestoreIt()
    {
        var log = new List<string>();
        var child = new Control { Name = "child" };

        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        log.Add($"before-dock.Anchor={child.Anchor}");

        // Docking forgets whatever anchor was set.
        child.Dock = DockStyle.Left;
        log.Add($"after-dock.Anchor={child.Anchor}");

        // Undocking does not bring the forgotten anchor back: it stays at the default.
        child.Dock = DockStyle.None;
        log.Add($"after-undock.Anchor={child.Anchor}");

        return log;
    }

    // Padding.

    private static IReadOnlyList<string> PaddingOnScrollableControlShrinksDisplayRectangle()
    {
        var parent = new ScrollableControl { Name = "parent", Size = new Size(100, 80) };
        parent.Padding = new Padding(10);
        var child = new Control { Name = "child", Dock = DockStyle.Fill };
        parent.Controls.Add(child);
        parent.PerformLayout();

        return [$"child.Bounds={child.Bounds}"];
    }

    private static IReadOnlyList<string> PaddingAllReportsMinusOneAfterOneSideSet()
    {
        var log = new List<string>();
        var padding = new Padding(4);
        log.Add($"after-ctor.All={padding.All}");

        // Setting one side on its own, even back to the same value every side already had, makes
        // the padding stop being "all sides together": All now answers -1.
        padding.Top = 4;
        log.Add($"after-set-top.All={padding.All}");
        log.Add($"after-set-top.Padding={padding}");

        var control = new Control { Padding = padding };
        log.Add($"control.Padding.All={control.Padding.All}");

        return log;
    }

    private static IReadOnlyList<string> PaddingClampsNegativeSides()
    {
        var log = new List<string>();
        var control = new Control();

        // Four equal, negative sides are still "all sides together", so they clamp to Padding.Empty.
        control.Padding = new Padding(-5);
        log.Add($"after-uniform-negative.Padding={control.Padding}");

        // A mix of negative and positive sides clamps only the negative ones to zero.
        control.Padding = new Padding(-3, 5, -2, 8);
        log.Add($"after-mixed-negative.Padding={control.Padding}");

        return log;
    }

    // MinimumSize and MaximumSize.

    private static IReadOnlyList<string> MinimumSizeRaisesSizeAndMaximum()
    {
        var log = new List<string>();
        var control = new Control { Size = new Size(50, 50) };

        control.MaximumSize = new Size(40, 40);
        log.Add($"after-maximum.Size={control.Size}");

        // Raising the minimum above a maximum that is already set raises that maximum too.
        control.MinimumSize = new Size(60, 60);
        log.Add($"after-minimum.Size={control.Size}");
        log.Add($"after-minimum.MaximumSize={control.MaximumSize}");

        return log;
    }

    private static IReadOnlyList<string> MaximumSizeLowersSizeAndMinimum()
    {
        var log = new List<string>();
        var control = new Control { Size = new Size(50, 50) };

        control.MinimumSize = new Size(30, 30);
        control.MaximumSize = new Size(20, 20);
        log.Add($"after-maximum.Size={control.Size}");
        log.Add($"after-maximum.MinimumSize={control.MinimumSize}");

        return log;
    }

    // AutoSize.

    private static IReadOnlyList<string> AutoSizeControlPreferredSizeIsSpecifiedNotCurrentBounds()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(200, 100) };
        var child = new Control { Name = "child", Bounds = new Rectangle(0, 0, 40, 20) };
        child.AutoSize = true;
        child.Dock = DockStyle.Top;
        parent.Controls.Add(child);
        parent.PerformLayout();

        // Docking stretched the child's width to the parent's, but its preferred size still
        // answers the size last specified by the user (40x20), not the current, docked bounds.
        log.Add($"child.Bounds={child.Bounds}");
        log.Add($"child.GetPreferredSize={child.GetPreferredSize(Size.Empty)}");

        return log;
    }

    // The tree: Controls.Add/Remove, SetChildIndex, ParentChanged.

    private static IReadOnlyList<string> ControlAddedAndRemovedEvents()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent" };
        var child = new Control { Name = "child" };

        parent.ControlAdded += ControlLogger(log, "parent", "ControlAdded");
        parent.ControlRemoved += ControlLogger(log, "parent", "ControlRemoved");

        parent.Controls.Add(child);
        parent.Controls.Remove(child);

        return log;
    }

    private static IReadOnlyList<string> SetChildIndexChangesDockOrder()
    {
        var log = new List<string>();
        var parent = new Control { Name = "parent", Size = new Size(100, 60) };
        var first = new Control { Name = "first", Dock = DockStyle.Top, Height = 10 };
        var second = new Control { Name = "second", Dock = DockStyle.Top, Height = 10 };
        parent.Controls.Add(first);
        parent.Controls.Add(second);
        parent.PerformLayout();
        log.Add($"before.first.Bounds={first.Bounds}");
        log.Add($"before.second.Bounds={second.Bounds}");

        // Moving first to the back (index -1 means the back) makes it dock first instead of second.
        parent.Controls.SetChildIndex(first, -1);
        parent.PerformLayout();
        log.Add($"after.first.Bounds={first.Bounds}");
        log.Add($"after.second.Bounds={second.Bounds}");

        return log;
    }

    private static IReadOnlyList<string> ParentChangedEventOnReparenting()
    {
        var log = new List<string>();
        var firstParent = new Control { Name = "firstParent" };
        var secondParent = new Control { Name = "secondParent" };
        var child = new Control { Name = "child" };

        child.ParentChanged += Logger(log, "child.ParentChanged");

        firstParent.Controls.Add(child);
        log.Add($"after-first-add.Parent={child.Parent?.Name}");

        // Adding to another container's Controls removes it from the first one, in one move.
        secondParent.Controls.Add(child);
        log.Add($"after-second-add.Parent={child.Parent?.Name}");
        log.Add($"firstParent.Controls.Count={firstParent.Controls.Count}");

        return log;
    }

    private static IReadOnlyList<string> FormStartsHiddenAndCascadesVisibilityToChildren()
    {
        var log = new List<string>();
        var form = new Form { Name = "form", Size = new Size(200, 150) };
        var child = new Control { Name = "child" };
        form.Controls.Add(child);

        // A form is a top-level control and starts hidden until shown: a child's own Visible flag
        // is true by default, but it reports false until the form itself becomes visible.
        log.Add($"before-show.form.Visible={form.Visible}");
        log.Add($"before-show.child.Visible={child.Visible}");

        form.Visible = true;
        log.Add($"after-show.form.Visible={form.Visible}");
        log.Add($"after-show.child.Visible={child.Visible}");

        return log;
    }
}
