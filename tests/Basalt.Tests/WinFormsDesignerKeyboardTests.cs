using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// The designer surface's keyboard: arrows, Shift+arrows, Tab, Esc, Ctrl+A, Delete and Backspace, the lock and the zoom.
/// Each test raises the key on the surface itself, as the window delivers it when the surface has focus.
/// </summary>
public sealed class WinFormsDesignerKeyboardTests
{
    private static (Window Window, WinFormsDesignSurface Surface, Forms.Form Form, Forms.Panel Panel, Forms.Button Button)
        Open()
    {
        var form = new Forms.Form
        {
            ClientSize = new Drawing.Size(220, 160),
            BackColor = Drawing.Color.White,
            FormBorderStyle = Forms.FormBorderStyle.None,
        };
        var panel = new Forms.Panel { Name = "Panel1", Bounds = new Drawing.Rectangle(100, 90, 100, 60) };
        var button = new Forms.Button { Name = "Button1", Bounds = new Drawing.Rectangle(10, 10, 40, 25), TabIndex = 0 };
        form.Controls.Add(button);
        form.Controls.Add(panel);
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Width = 320, Height = 250, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, surface, form, panel, button);
    }

    private static void Press(WinFormsDesignSurface surface, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        surface.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ShiftArrowResizesOnePixelInsteadOfMoving()
    {
        var (window, surface, _, _, button) = Open();
        surface.Select(button);

         Press(surface, Key.Right, KeyModifiers.Shift);

        Assert.Equal(new Drawing.Point(10, 10), button.Location);
        Assert.Equal(41, button.Width);
         Press(surface, Key.Down, KeyModifiers.Shift);
        Assert.Equal(26, button.Height);
    }

    [AvaloniaFact]
    public void CtrlArrowNudgesOnePixelEvenWhenSnapping()
    {
        var (window, surface, _, _, button) = Open();
        surface.SnapToGrid = true;
        surface.GridSize = new Drawing.Size(8, 8);
        surface.Select(button);

         Press(surface, Key.Right, KeyModifiers.Control);

        Assert.Equal(new Drawing.Point(11, 10), button.Location);
    }

    [AvaloniaFact]
    public void BareArrowMovesByTheGridWhenSnapping()
    {
        var (window, surface, _, _, button) = Open();
        surface.SnapToGrid = true;
        surface.GridSize = new Drawing.Size(8, 8);
        surface.Select(button);

         Press(surface, Key.Down);

        Assert.Equal(new Drawing.Point(10, 18), button.Location);
    }

    [AvaloniaFact]
    public void BareArrowMovesOnePixelWithoutSnapping()
    {
        var (window, surface, _, _, button) = Open();
        surface.SnapToGrid = false;
        surface.Select(button);

         Press(surface, Key.Left);

        Assert.Equal(new Drawing.Point(9, 10), button.Location);
    }

    [AvaloniaFact]
    public void TabAndShiftTabCycleTheSelectionInTabOrder()
    {
        var (window, surface, form, panel, button) = Open();
        var second = new Forms.Button { Name = "Button2", Bounds = new Drawing.Rectangle(60, 10, 40, 25), TabIndex = 1 };
        form.Controls.Add(second);
        // Order: Button1 (0), Button2 (1), Panel1 (2). The panel is an ordinary tabbable control.
        panel.TabIndex = 2;
        surface.RefreshForm();
        surface.Select(button);

        Press(surface, Key.Tab);
        Assert.Same(second, surface.SelectedControl);

        Press(surface, Key.Tab);
        Assert.Same(panel, surface.SelectedControl);

        // Forward past the last control wraps to the first.
        Press(surface, Key.Tab);
        Assert.Same(button, surface.SelectedControl);

        // Backward past the first control wraps to the last.
        Press(surface, Key.Tab, KeyModifiers.Shift);
        Assert.Same(panel, surface.SelectedControl);
    }

    [AvaloniaFact]
    public void BackspaceDoesNotDeleteButDeleteDoes()
    {
        var (window, surface, _, _, button) = Open();
        var requests = new List<Forms.Control>();
        surface.DeleteRequested += requests.Add;
        surface.Select(button);

         Press(surface, Key.Back);
        Assert.Empty(requests);

         Press(surface, Key.Delete);
        Assert.Same(button, Assert.Single(requests));
    }

    [AvaloniaFact]
    public void EscSelectsTheContainerThenTheForm()
    {
        var (window, surface, form, panel, _) = Open();
        var inner = new Forms.Button { Name = "Inner", Bounds = new Drawing.Rectangle(5, 5, 30, 20) };
        panel.Controls.Add(inner);
        surface.RefreshForm();
        surface.Select(inner);

         Press(surface, Key.Escape);
        Assert.Same(panel, surface.SelectedControl);

         Press(surface, Key.Escape);
        Assert.Same(form, surface.SelectedControl);

         Press(surface, Key.Escape);
        Assert.Same(form, surface.SelectedControl);
    }

    [AvaloniaFact]
    public void CtrlAAndCmdASelectTheFormsControls()
    {
        var (window, surface, form, _, _) = Open();
        surface.Select(null);

         Press(surface, Key.A, KeyModifiers.Control);
        Assert.Equal(form.Controls.Count, surface.SelectedControls.Count);

        surface.Select(null);
         Press(surface, Key.A, KeyModifiers.Meta);
        Assert.Equal(form.Controls.Count, surface.SelectedControls.Count);
    }

    [AvaloniaFact]
    public void LockedControlsCannotBeMovedByKeyOrPointer()
    {
        var (window, surface, _, _, button) = Open();
        surface.LockControls = true;
        surface.Select(button);

         Press(surface, Key.Right);
         Press(surface, Key.Right, KeyModifiers.Shift);
        Assert.Equal(new Drawing.Point(10, 10), button.Location);
        Assert.Equal(40, button.Width);

        var at = new Point(12 + 10 + 20, 12 + 10 + 12);
        var to = new Point(at.X + 30, at.Y + 30);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Drawing.Point(10, 10), button.Location);

        // The same gesture at the same point moves the control once the lock is off, so the locked result above is not a
        // drag that never registered.
        surface.LockControls = false;
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(new Drawing.Point(10, 10), button.Location);
    }

    [AvaloniaFact]
    public void ZoomScalesTheSurfaceAndClamps()
    {
        var (window, surface, _, _, _) = Open();

        surface.Zoom = 2;
        Assert.Equal(2, surface.Zoom);
        Assert.Equal(2, Assert.IsType<ScaleTransform>(surface.GetVisualDescendants()
            .OfType<Canvas>().First(canvas => canvas.RenderTransform is ScaleTransform).RenderTransform).ScaleX);

        surface.Zoom = 100;
        Assert.Equal(WinFormsDesignSurface.MaximumZoom, surface.Zoom);
    }
}
