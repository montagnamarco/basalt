using System.Drawing;
using Avalonia.Input;
using AvaloniaKeyEventArgs = Avalonia.Input.KeyEventArgs;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// Routes Avalonia's pointer and key events to the controls of a window, with
/// the rules of Windows: the topmost control under the pointer gets the
/// mouse unless one has captured it, and keys go through the window's
/// pre-processing before the focused control sees them.
/// </summary>
internal static class FormsInput
{
    /// <summary>The control that holds the mouse capture, if any.</summary>
    public static Control? CapturedControl { get; set; }

    /// <summary>
    /// Set while Control.DoDragDrop runs its own loop: pointer events still
    /// update <see cref="Buttons"/> and <see cref="ScreenPosition"/> below, but
    /// are not dispatched as ordinary mouse messages, since the drag loop (not
    /// the control under the pointer) decides what they mean.
    /// </summary>
    public static bool Dragging { get; set; }

    public static MouseButtons Buttons { get; private set; }

    public static Point ScreenPosition { get; private set; }

    public static Keys Modifiers { get; private set; }

    /// <summary>The keys held down now, for code that asks the state of one key as GetKeyState does.</summary>
    private static readonly HashSet<Keys> HeldKeys = [];

    public static bool IsKeyHeld(Keys key) => HeldKeys.Contains(key);

    /// <summary>A window gained or lost the keyboard: releases it never saw must not leave keys held.</summary>
    public static void ForgetHeldKeys() => HeldKeys.Clear();

    /// <summary>The control the pointer is over, for MouseEnter and MouseLeave.</summary>
    private static Control? s_hovered;

    public static void PointerMoved(Control window, FormsCanvas canvas, PointerEventArgs e)
    {
        Guard(window, () =>
        {
            var point = ToClient(canvas, e);
            Track(window, point, e.GetCurrentPoint(canvas).Properties, e.KeyModifiers);
            if (Dragging) return;

            var (target, location) = Target(window, point);

            // A press on a scroll bar of the frame follows the pointer by itself.
            if (target.IsTrackingNonClient)
            {
                target.NonClientMouseMove(target.ClientToOuter(location));
                return;
            }

            UpdateHover(window, point);
            target.RaiseMouseMove(Buttons, location);
        });
    }

    public static void PointerPressed(Control window, FormsCanvas canvas, PointerPressedEventArgs e)
    {
        Guard(window, () =>
        {
            var point = ToClient(canvas, e);
            var properties = e.GetCurrentPoint(canvas).Properties;
            Track(window, point, properties, e.KeyModifiers);
            if (Dragging) return;

            var button = ChangedButton(properties.PointerUpdateKind);
            if (button == MouseButtons.None) return;

            // A press outside an open menu or drop-down closes it, and does nothing else.
            if (Popups.DismissForPressIn(window)) return;

            // The canvas keeps Avalonia's capture while a button is down, so a drag leaving the window still reports here.
            e.Pointer.Capture(canvas);
            canvas.Focus();

            var (target, location) = Target(window, point);

            // A press inside a form that is not a top-level window (an MDI child) activates it first.
            if (button == MouseButtons.Left) Form.ActivateChildFormAround(target, window);

            // On the frame (a scroll bar), not in the client area: the window handles it, no mouse events.
            if (!target.ClientRectangle.Contains(location) && target != window)
            {
                if (target.NonClientMouseDown(button, target.ClientToOuter(location))) CapturedControl = target;

                return;
            }

            target.RaiseMouseDown(button, Math.Min(e.ClickCount, 2), location);
        });
    }

    public static void PointerReleased(Control window, FormsCanvas canvas, PointerReleasedEventArgs e)
    {
        Guard(window, () =>
        {
            var point = ToClient(canvas, e);
            var properties = e.GetCurrentPoint(canvas).Properties;
            var button = ChangedButton(properties.PointerUpdateKind);
            Track(window, point, properties, e.KeyModifiers);
            if (Dragging) return;

            if (button == MouseButtons.None) return;

            var (target, location) = Target(window, point);

            if (target.IsTrackingNonClient)
            {
                target.NonClientMouseUp(target.ClientToOuter(location));
                if (CapturedControl == target) CapturedControl = null;
                if (Buttons == MouseButtons.None) e.Pointer.Capture(null);

                return;
            }

            var (under, _) = window.HitTest(point);
            target.RaiseMouseUp(button, 1, location, releasedOverThisControl: under == target);

            if (Buttons == MouseButtons.None) e.Pointer.Capture(null);
            UpdateHover(window, point);
        });
    }

    public static void PointerExited(Control window)
    {
        if (CapturedControl is not null || s_hovered is null) return;

        Guard(window, () =>
        {
            var left = s_hovered;
            s_hovered = null;
            if (left is { IsDisposed: false }) left.RaiseMouseLeave();
        });
    }

    public static void PointerWheel(Control window, FormsCanvas canvas, PointerWheelEventArgs e)
    {
        Guard(window, () =>
        {
            var point = ToClient(canvas, e);
            Track(window, point, e.GetCurrentPoint(canvas).Properties, e.KeyModifiers);

            // 120 per notch, positive away from the user, as WM_MOUSEWHEEL reports it.
            var delta = (int)Math.Round(e.Delta.Y * 120);
            if (delta == 0) return;

            var (target, location) = Target(window, point);

            // Unhandled, the wheel goes to the parent, as Windows forwards WM_MOUSEWHEEL.
            for (Control? control = target; control is not null; control = control.Parent)
            {
                if (control.RaiseMouseWheel(location, delta)) break;

                location = control.Parent is null ? location : control.Parent.PointToClient(control.PointToScreen(location));
                if (control == window) break;
            }
        });
    }

    public static void CaptureLost(Control window)
    {
        if (CapturedControl is { IsTrackingNonClient: true } tracking) tracking.DefaultCaptureLost();

        if (CapturedControl is { } captured && captured.TopLevelControl == window) captured.Capture = false;

        Buttons = MouseButtons.None;
    }

    public static bool KeyDown(Control window, AvaloniaKeyEventArgs e)
    {
        Modifiers = KeyMapping.ToModifiers(e.KeyModifiers);

        var keyCode = KeyMapping.ToKeys(e.Key);
        if (keyCode == Keys.None) return false;

        HeldKeys.Add(keyCode);

        // During DoDragDrop the drag loop reads the keys (Escape cancels); controls do not see them.
        if (Dragging) return true;

        return Guard(window, () =>
        {
            FormsInputState.SuppressNextCharacter = false;

            if (CapturedControl is { } captured && captured.TopLevelControl == window && captured.KeyDownWhileCaptured(keyCode | Modifiers)) return true;

            var target = KeyTarget(window);
            var messageKind = (Modifiers & Keys.Alt) != 0 ? WindowMessages.SystemKeyDown : WindowMessages.KeyDown;
            var message = Message.Create(target.Handle, messageKind, (IntPtr)(int)keyCode, IntPtr.Zero);
            var handled = target.DispatchKeyMessage(ref message);

            // Keys that type no text still send a character to the control, as WM_CHAR does on Windows.
            if (!FormsInputState.SuppressNextCharacter && KeyMapping.ControlCharacter(keyCode, Modifiers) is { } character)
            {
                SendCharacter(window, character);
            }

            return handled;
        });
    }

    public static bool KeyUp(Control window, AvaloniaKeyEventArgs e)
    {
        Modifiers = KeyMapping.ToModifiers(e.KeyModifiers);

        var keyCode = KeyMapping.ToKeys(e.Key);
        if (keyCode == Keys.None) return false;

        HeldKeys.Remove(keyCode);
        if (Dragging) return true;

        // SuppressKeyPress removes the character of that one key press; a key that typed nothing
        // (Enter on some platforms, a synthesized press) must not leave it waiting for the next text.
        FormsInputState.SuppressNextCharacter = false;

        return Guard(window, () =>
        {
            var target = KeyTarget(window);
            var messageKind = (Modifiers & Keys.Alt) != 0 ? WindowMessages.SystemKeyUp : WindowMessages.KeyUp;
            var message = Message.Create(target.Handle, messageKind, (IntPtr)(int)keyCode, IntPtr.Zero);

            return target.DispatchKeyMessage(ref message);
        });
    }

    public static bool TextInput(Control window, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return false;

        return Guard(window, () =>
        {
            if (FormsInputState.SuppressNextCharacter)
            {
                FormsInputState.SuppressNextCharacter = false;
                return true;
            }

            foreach (var character in e.Text) SendCharacter(window, character);

            return true;
        });
    }

    /// <summary>Where keys go: an open popup that takes them (a menu), else the window's focused control.</summary>
    private static Control KeyTarget(Control window)
    {
        if (Popups.KeyboardOwnerFor(window) is { } popup) return popup.WindowFocus ?? popup;

        return window.WindowFocus ?? window;
    }

    private static void SendCharacter(Control window, char character)
    {
        var target = KeyTarget(window);
        var message = Message.Create(target.Handle, WindowMessages.Char, (IntPtr)character, IntPtr.Zero);
        target.DispatchKeyMessage(ref message);
    }

    /// <summary>
    /// Runs input or paint code for a window; an exception goes to
    /// Application.ThreadException as it would from a WinForms message loop,
    /// instead of tearing down the window system's own loop.
    /// </summary>
    public static void Guard(Control window, Action action) => Guard(window, () =>
    {
        action();
        return true;
    });

    public static T? Guard<T>(Control window, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (Application.HandlesThreadExceptions)
        {
            Application.OnThreadException(exception);
            return default;
        }
    }

    private static (Control Target, Point Location) Target(Control window, Point clientPoint)
    {
        if (CapturedControl is { } captured && captured.TopLevelControl == window && !captured.IsDisposed)
        {
            return (captured, captured.PointToClient(window.PointToScreen(clientPoint)));
        }

        return window.HitTest(clientPoint);
    }

    private static void UpdateHover(Control window, Point clientPoint)
    {
        // While a control has the capture, enter and leave wait, as on Windows.
        if (CapturedControl is not null) return;

        var (under, _) = window.HitTest(clientPoint);
        if (under == s_hovered) return;

        var left = s_hovered;
        s_hovered = under;

        // A control disposed while under the pointer has no leave to report.
        if (left is { IsDisposed: false }) left.RaiseMouseLeave();
        under.RaiseMouseEnter();
        RefreshCursor();
    }

    /// <summary>Shows the cursor of the control under the pointer, or the application's current cursor.</summary>
    public static void RefreshCursor()
    {
        // A cursor set from another thread (a worker, or code that runs before the window exists) is applied on the UI thread.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCursor);
            return;
        }

        var control = CapturedControl ?? s_hovered;
        if (control?.TopLevelControl?.Canvas is not { } canvas) return;

        var cursor = Cursor.Current ?? control.EffectiveCursor;
        canvas.Cursor = PlatformCursor(cursor.Standard);
    }

    private static readonly Dictionary<StandardCursorType, Avalonia.Input.Cursor> PlatformCursors = [];

    /// <summary>One platform cursor per shape, reused: each one holds a native resource.</summary>
    private static Avalonia.Input.Cursor PlatformCursor(StandardCursorType type)
    {
        if (!PlatformCursors.TryGetValue(type, out var cursor))
        {
            cursor = new Avalonia.Input.Cursor(type);
            PlatformCursors[type] = cursor;
        }

        return cursor;
    }

    private static void Track(Control window, Point clientPoint, PointerPointProperties properties, KeyModifiers modifiers)
    {
        ScreenPosition = window.PointToScreen(clientPoint);
        Modifiers = KeyMapping.ToModifiers(modifiers);

        var buttons = MouseButtons.None;
        if (properties.IsLeftButtonPressed) buttons |= MouseButtons.Left;
        if (properties.IsRightButtonPressed) buttons |= MouseButtons.Right;
        if (properties.IsMiddleButtonPressed) buttons |= MouseButtons.Middle;
        if (properties.IsXButton1Pressed) buttons |= MouseButtons.XButton1;
        if (properties.IsXButton2Pressed) buttons |= MouseButtons.XButton2;
        Buttons = buttons;
    }

    private static MouseButtons ChangedButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButtons.Left,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButtons.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButtons.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => MouseButtons.XButton1,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };

    /// <summary>The pointer in the window's client coordinates: one device-independent pixel is one WinForms pixel.</summary>
    private static Point ToClient(FormsCanvas canvas, PointerEventArgs e)
    {
        var position = e.GetPosition(canvas);

        return new Point((int)Math.Floor(position.X), (int)Math.Floor(position.Y));
    }
}
