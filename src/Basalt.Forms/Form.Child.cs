using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// A form that is not a top-level window: an MDI child, or a form placed in a
/// panel with TopLevel set to false. Windows draws its frame and title bar
/// inside the parent and moves, sizes, minimizes and maximizes it there; here
/// the classic skin draws them and this part handles the mouse on them.
/// </summary>
public partial class Form
{
    /// <summary>The smallest window the sizing frame leaves: room for the title bar and its buttons.</summary>
    private static readonly Size MinimumTrackSize = new(112, 27);

    /// <summary>The width Windows gives a minimized child: its title bar and buttons alone.</summary>
    private const int MinimizedWidth = 160;

    /// <summary>What the mouse is holding on the frame: a button, the title bar, or an edge being dragged.</summary>
    private CaptionHitArea _frameTracking;

    /// <summary>The button drawn pressed: the one being held, while the pointer is still over it.</summary>
    private CaptionHitArea _framePressed;

    private Point _dragStartInParent;
    private Rectangle _dragStartBounds;

    /// <summary>The bounds to come back to from minimized or maximized.</summary>
    private Rectangle _restoreBounds;

    /// <summary>For a child that is not an MDI child: whether it was the last of its siblings to be activated.</summary>
    private bool _activeChild;

    /// <summary>Whether the title bar is drawn in the active colours.</summary>
    internal bool IsActiveChild => MdiParent is { } parent ? parent.ActiveMdiChild == this : _activeChild;

    internal override WindowCaption? NonClientCaption => GetTopLevel()
        ? null
        : new WindowCaption(Text, IsActiveChild, ControlBox, MinimizeBox, MaximizeBox,
            _windowState == FormWindowState.Maximized, _windowState == FormWindowState.Minimized, _framePressed,
            ShowIcon ? _icon : null);

    /// <summary>The frame and title bar are drawn with the child: redraw them.</summary>
    internal void InvalidateFrame() => Invalidate();

    /// <summary>Brings the child to the front, draws it active and the others inactive, and gives it the keyboard.</summary>
    internal void ActivateAsChild(bool takeFocus = true)
    {
        BringToFront();

        if (MdiParent is { } parent)
        {
            parent.SetActiveMdiChildCore(this);
        }
        else if (!_activeChild)
        {
            foreach (var sibling in Parent?.Controls.OfType<Form>() ?? [])
            {
                if (sibling == this || !sibling._activeChild) continue;

                sibling._activeChild = false;
                sibling.InvalidateFrame();
            }

            _activeChild = true;
            InvalidateFrame();
        }

        if (takeFocus && !ContainsFocus) FocusFirstChild();
    }

    /// <summary>The control that had the keyboard last time, else the first one in tab order, else the form itself.</summary>
    private void FocusFirstChild()
    {
        if (ActiveControl is { CanFocus: true } active)
        {
            active.Focus();
            return;
        }

        if (!SelectNextControl(null, forward: true, tabStopOnly: true, nested: true, wrap: false)) Focus();
    }

    /// <summary>A press anywhere in a child form, frame or client, activates it, as Windows does.</summary>
    internal static void ActivateChildFormAround(Control target, Control window)
    {
        for (var control = target; control is not null && control != window; control = control.Parent)
        {
            if (control is Form { TopLevel: false, Visible: true } child)
            {
                if (!child.IsActiveChild) child.ActivateAsChild(takeFocus: false);
                return;
            }
        }
    }

    internal override bool NonClientMouseDown(MouseButtons button, Point outer)
    {
        if (GetTopLevel() || button != MouseButtons.Left || NonClientCaption is not { } caption) return false;

        ActivateAsChild();

        var parameters = CreateParams;
        var hit = CaptionLayout.HitTest(outer, Size, parameters.Style, parameters.ExStyle, caption);
        if (hit == CaptionHitArea.None) return false;

        // A maximized window does not move; its title bar only takes clicks on its buttons.
        if (hit == CaptionHitArea.Caption && _windowState == FormWindowState.Maximized) return false;

        _frameTracking = hit;
        _dragStartInParent = new Point(Left + outer.X, Top + outer.Y);
        _dragStartBounds = Bounds;

        if (IsCaptionButton(hit))
        {
            _framePressed = hit;
            InvalidateFrame();
        }
        else
        {
            OnResizeBegin(EventArgs.Empty);
        }

        return true;
    }

    internal override void NonClientMouseMove(Point outer)
    {
        if (_frameTracking == CaptionHitArea.None) return;

        if (IsCaptionButton(_frameTracking))
        {
            var parameters = CreateParams;
            var over = NonClientCaption is { } caption && CaptionLayout.HitTest(outer, Size, parameters.Style, parameters.ExStyle, caption) == _frameTracking;
            var pressed = over ? _frameTracking : CaptionHitArea.None;
            if (pressed == _framePressed) return;

            _framePressed = pressed;
            InvalidateFrame();
            return;
        }

        var inParent = new Point(Left + outer.X, Top + outer.Y);
        var dx = inParent.X - _dragStartInParent.X;
        var dy = inParent.Y - _dragStartInParent.Y;

        Bounds = _frameTracking == CaptionHitArea.Caption
            ? new Rectangle(_dragStartBounds.X + dx, _dragStartBounds.Y + dy, _dragStartBounds.Width, _dragStartBounds.Height)
            : ResizedBounds(_dragStartBounds, _frameTracking, dx, dy);
    }

    internal override void NonClientMouseUp(Point outer)
    {
        var tracking = _frameTracking;
        var released = _framePressed;

        _frameTracking = CaptionHitArea.None;
        _framePressed = CaptionHitArea.None;
        if (IsCaptionButton(tracking)) InvalidateFrame();
        else if (tracking != CaptionHitArea.None) OnResizeEnd(EventArgs.Empty);

        // A button acts only when released over it.
        if (released == tracking) PerformCaptionButton(tracking);
    }

    internal override bool IsTrackingNonClient => !GetTopLevel() && _frameTracking != CaptionHitArea.None;

    internal override void DefaultCaptureLost()
    {
        base.DefaultCaptureLost();

        if (_frameTracking == CaptionHitArea.None) return;

        var resizing = !IsCaptionButton(_frameTracking);
        _frameTracking = CaptionHitArea.None;
        _framePressed = CaptionHitArea.None;
        InvalidateFrame();
        if (resizing) OnResizeEnd(EventArgs.Empty);
    }

    private void PerformCaptionButton(CaptionHitArea button)
    {
        switch (button)
        {
            case CaptionHitArea.CloseButton:
                Close();
                break;

            case CaptionHitArea.MaximizeButton when MaximizeBox:
                WindowState = _windowState == FormWindowState.Normal ? FormWindowState.Maximized : FormWindowState.Normal;
                break;

            case CaptionHitArea.MinimizeButton when MinimizeBox:
                WindowState = _windowState == FormWindowState.Minimized ? FormWindowState.Normal : FormWindowState.Minimized;
                break;
        }
    }

    private static bool IsCaptionButton(CaptionHitArea hit) =>
        hit is CaptionHitArea.CloseButton or CaptionHitArea.MaximizeButton or CaptionHitArea.MinimizeButton;

    /// <summary>The bounds an edge or corner drag gives, never smaller than the title bar needs or MinimumSize asks.</summary>
    private Rectangle ResizedBounds(Rectangle start, CaptionHitArea edge, int dx, int dy)
    {
        var minimumWidth = Math.Max(MinimumTrackSize.Width, MinimumSize.Width);
        var minimumHeight = Math.Max(MinimumTrackSize.Height, MinimumSize.Height);

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        if (edge is CaptionHitArea.Left or CaptionHitArea.TopLeft or CaptionHitArea.BottomLeft) left = Math.Min(start.Left + dx, right - minimumWidth);
        if (edge is CaptionHitArea.Right or CaptionHitArea.TopRight or CaptionHitArea.BottomRight) right = Math.Max(start.Right + dx, left + minimumWidth);
        if (edge is CaptionHitArea.Top or CaptionHitArea.TopLeft or CaptionHitArea.TopRight) top = Math.Min(start.Top + dy, bottom - minimumHeight);
        if (edge is CaptionHitArea.Bottom or CaptionHitArea.BottomLeft or CaptionHitArea.BottomRight) bottom = Math.Max(start.Bottom + dy, top + minimumHeight);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>
    /// Moves the child for its new window state inside its parent: maximized fills the parent's
    /// client area with the side and bottom frame outside it, minimized shrinks to its title bar
    /// along the bottom, normal goes back where it was.
    /// </summary>
    private void ApplyChildWindowState(FormWindowState previous)
    {
        if (Parent is not { } parent) return;

        switch (_windowState)
        {
            case FormWindowState.Normal:
                Bounds = _restoreBounds;
                break;

            case FormWindowState.Maximized:
                Bounds = MaximizedChildBounds(parent);
                break;

            case FormWindowState.Minimized:
                Bounds = MinimizedBounds(parent);
                break;
        }

        InvalidateFrame();
    }

    /// <summary>
    /// The client area of the parent, with the frame outside it on the left, right and bottom.
    /// The title bar stays visible: Windows would move its buttons into the parent's menu bar,
    /// which is not done yet, and without them a maximized child could not be restored.
    /// </summary>
    internal Rectangle MaximizedChildBounds(Control parent)
    {
        var client = parent.ClientRectangle;
        var frame = NonClient;

        return new Rectangle(client.Left - frame.Left, client.Top, client.Width + frame.Horizontal, client.Height + frame.Bottom);
    }

    private Rectangle MinimizedBounds(Control parent)
    {
        var height = NonClient.Top + NonClient.Bottom;
        var client = parent.ClientRectangle;

        // Next to the children already minimized, left to right along the bottom.
        var slot = parent.Controls.OfType<Form>().Count(form => form != this && form._windowState == FormWindowState.Minimized);

        return new Rectangle(client.Left + slot * MinimizedWidth, client.Bottom - height, MinimizedWidth, height);
    }
}
