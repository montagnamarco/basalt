using System.Drawing;
using Avalonia.Input;
using AvaloniaKeyEventArgs = Avalonia.Input.KeyEventArgs;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// Routes Avalonia's pointer and key events to the controls of a form, with
/// the rules of Windows: the topmost control under the pointer gets the
/// mouse unless one has captured it, and keys go through the form's
/// pre-processing before the focused control sees them.
/// </summary>
internal static class FormsInput
{
    /// <summary>The control that holds the mouse capture, if any.</summary>
    public static Control? CapturedControl { get; set; }

    public static MouseButtons Buttons { get; private set; }

    public static Point ScreenPosition { get; private set; }

    public static Keys Modifiers { get; private set; }

    /// <summary>The control the pointer is over, for MouseEnter and MouseLeave.</summary>
    private static Control? s_hovered;

    public static void PointerMoved(Form form, FormsCanvas canvas, PointerEventArgs e)
    {
        Guard(form, () =>
        {
            var point = ToClient(canvas, e);
            Track(form, point, e.GetCurrentPoint(canvas).Properties, e.KeyModifiers);

            var (target, location) = Target(form, point);
            UpdateHover(form, point);
            target.RaiseMouseMove(Buttons, location);
        });
    }

    public static void PointerPressed(Form form, FormsCanvas canvas, PointerPressedEventArgs e)
    {
        Guard(form, () =>
        {
            var point = ToClient(canvas, e);
            var properties = e.GetCurrentPoint(canvas).Properties;
            Track(form, point, properties, e.KeyModifiers);

            var button = ChangedButton(properties.PointerUpdateKind);
            if (button == MouseButtons.None) return;

            // The canvas keeps Avalonia's capture while a button is down, so a drag leaving the window still reports here.
            e.Pointer.Capture(canvas);
            canvas.Focus();

            var (target, location) = Target(form, point);
            target.RaiseMouseDown(button, Math.Min(e.ClickCount, 2), location);
        });
    }

    public static void PointerReleased(Form form, FormsCanvas canvas, PointerReleasedEventArgs e)
    {
        Guard(form, () =>
        {
            var point = ToClient(canvas, e);
            var properties = e.GetCurrentPoint(canvas).Properties;
            var button = ChangedButton(properties.PointerUpdateKind);
            Track(form, point, properties, e.KeyModifiers);

            if (button == MouseButtons.None) return;

            var (target, location) = Target(form, point);
            var (under, _) = form.HitTest(point);
            target.RaiseMouseUp(button, 1, location, releasedOverThisControl: under == target);

            if (Buttons == MouseButtons.None) e.Pointer.Capture(null);
            UpdateHover(form, point);
        });
    }

    public static void PointerExited(Form form)
    {
        if (CapturedControl is not null || s_hovered is null) return;

        Guard(form, () =>
        {
            var left = s_hovered;
            s_hovered = null;
            if (left is { IsDisposed: false }) left.RaiseMouseLeave();
        });
    }

    public static void PointerWheel(Form form, FormsCanvas canvas, PointerWheelEventArgs e)
    {
        Guard(form, () =>
        {
            var point = ToClient(canvas, e);
            Track(form, point, e.GetCurrentPoint(canvas).Properties, e.KeyModifiers);

            // 120 per notch, positive away from the user, as WM_MOUSEWHEEL reports it.
            var delta = (int)Math.Round(e.Delta.Y * 120);
            if (delta == 0) return;

            var (target, location) = Target(form, point);

            // Unhandled, the wheel goes to the parent, as Windows forwards WM_MOUSEWHEEL.
            for (Control? control = target; control is not null; control = control.Parent)
            {
                if (control.RaiseMouseWheel(location, delta)) break;

                location = control.Parent is null ? location : control.Parent.PointToClient(control.PointToScreen(location));
                if (control == form) break;
            }
        });
    }

    public static void CaptureLost(Form form)
    {
        if (CapturedControl is { } captured && captured.TopLevelControl == form) captured.Capture = false;

        Buttons = MouseButtons.None;
    }

    public static bool KeyDown(Form form, AvaloniaKeyEventArgs e)
    {
        Modifiers = KeyMapping.ToModifiers(e.KeyModifiers);

        var keyCode = KeyMapping.ToKeys(e.Key);
        if (keyCode == Keys.None) return false;

        return Guard(form, () =>
        {
            FormsInputState.SuppressNextCharacter = false;

            var target = form.WindowFocus ?? form;
            var messageKind = (Modifiers & Keys.Alt) != 0 ? WindowMessages.SystemKeyDown : WindowMessages.KeyDown;
            var message = Message.Create(target.Handle, messageKind, (IntPtr)(int)keyCode, IntPtr.Zero);
            var handled = target.DispatchKeyMessage(ref message);

            // Keys that type no text still send a character to the control, as WM_CHAR does on Windows.
            if (!FormsInputState.SuppressNextCharacter && KeyMapping.ControlCharacter(keyCode, Modifiers) is { } character)
            {
                SendCharacter(form, character);
            }

            return handled;
        });
    }

    public static bool KeyUp(Form form, AvaloniaKeyEventArgs e)
    {
        Modifiers = KeyMapping.ToModifiers(e.KeyModifiers);

        var keyCode = KeyMapping.ToKeys(e.Key);
        if (keyCode == Keys.None) return false;

        return Guard(form, () =>
        {
            var target = form.WindowFocus ?? form;
            var messageKind = (Modifiers & Keys.Alt) != 0 ? WindowMessages.SystemKeyUp : WindowMessages.KeyUp;
            var message = Message.Create(target.Handle, messageKind, (IntPtr)(int)keyCode, IntPtr.Zero);

            return target.DispatchKeyMessage(ref message);
        });
    }

    public static bool TextInput(Form form, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return false;

        return Guard(form, () =>
        {
            if (FormsInputState.SuppressNextCharacter)
            {
                FormsInputState.SuppressNextCharacter = false;
                return true;
            }

            foreach (var character in e.Text) SendCharacter(form, character);

            return true;
        });
    }

    private static void SendCharacter(Form form, char character)
    {
        var target = form.WindowFocus ?? form;
        var message = Message.Create(target.Handle, WindowMessages.Char, (IntPtr)character, IntPtr.Zero);
        target.DispatchKeyMessage(ref message);
    }

    /// <summary>
    /// Runs input or paint code for a form; an exception goes to
    /// Application.ThreadException as it would from a WinForms message loop,
    /// instead of tearing down the window system's own loop.
    /// </summary>
    public static void Guard(Form form, Action action) => Guard(form, () =>
    {
        action();
        return true;
    });

    public static T? Guard<T>(Form form, Func<T> action)
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

    private static (Control Target, Point Location) Target(Form form, Point clientPoint)
    {
        if (CapturedControl is { } captured && captured.TopLevelControl == form && !captured.IsDisposed)
        {
            return (captured, captured.PointToClient(form.PointToScreen(clientPoint)));
        }

        return form.HitTest(clientPoint);
    }

    private static void UpdateHover(Form form, Point clientPoint)
    {
        // While a control has the capture, enter and leave wait, as on Windows.
        if (CapturedControl is not null) return;

        var (under, _) = form.HitTest(clientPoint);
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
        if (control?.TopLevelControl is not Form { Host: { } host }) return;

        var cursor = Cursor.Current ?? control.EffectiveCursor;
        host.Canvas.Cursor = PlatformCursor(cursor.Standard);
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

    private static void Track(Form form, Point clientPoint, PointerPointProperties properties, KeyModifiers modifiers)
    {
        ScreenPosition = form.PointToScreen(clientPoint);
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

    /// <summary>The pointer in the form's client coordinates: one device-independent pixel is one WinForms pixel.</summary>
    private static Point ToClient(FormsCanvas canvas, PointerEventArgs e)
    {
        var position = e.GetPosition(canvas);

        return new Point((int)Math.Floor(position.X), (int)Math.Floor(position.Y));
    }
}
