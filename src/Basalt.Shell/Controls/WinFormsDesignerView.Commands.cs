namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    /// <summary>
    /// Carries out a menu or shortcut command on this designer's surface. Layout commands reach the session as the layout
    /// toolbar's do, so each one is an undoable edit; view commands change only the surface.
    /// </summary>
    internal void RunDesignerCommand(WinFormsDesignerCommand command)
    {
        switch (command)
        {
            case WinFormsDesignerCommand.AlignLeft: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignLeft); break;
            case WinFormsDesignerCommand.AlignCenter: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignCenter); break;
            case WinFormsDesignerCommand.AlignRight: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignRight); break;
            case WinFormsDesignerCommand.AlignTop: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignTop); break;
            case WinFormsDesignerCommand.AlignMiddle: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignMiddle); break;
            case WinFormsDesignerCommand.AlignBottom: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignBottom); break;
            case WinFormsDesignerCommand.SameWidth: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.SameWidth); break;
            case WinFormsDesignerCommand.SameHeight: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.SameHeight); break;
            case WinFormsDesignerCommand.SameSize: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.SameSize); break;
            case WinFormsDesignerCommand.SpaceHorizontally: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.DistributeHorizontal); break;
            case WinFormsDesignerCommand.SpaceVertically: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.DistributeVertical); break;
            case WinFormsDesignerCommand.CenterHorizontally: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.CenterHorizontally); break;
            case WinFormsDesignerCommand.CenterVertically: _surface.ApplyLayoutCommand(WinFormsLayoutCommand.CenterVertically); break;

            case WinFormsDesignerCommand.BringToFront: _surface.RequestZOrder(bringToFront: true); break;
            case WinFormsDesignerCommand.SendToBack: _surface.RequestZOrder(bringToFront: false); break;
            case WinFormsDesignerCommand.LockControls: _surface.LockControls = !_surface.LockControls; break;

            case WinFormsDesignerCommand.SelectParent: _surface.SelectParent(); break;
            case WinFormsDesignerCommand.SelectAll: _surface.SelectAllControls(); break;
            case WinFormsDesignerCommand.Delete: _surface.RequestDelete(); break;

            case WinFormsDesignerCommand.ZoomIn: _surface.Zoom *= 1.25; break;
            case WinFormsDesignerCommand.ZoomOut: _surface.Zoom /= 1.25; break;
            case WinFormsDesignerCommand.ZoomReset: _surface.Zoom = 1; break;
            case WinFormsDesignerCommand.ZoomToFit: _surface.ZoomToFit(); break;
            case WinFormsDesignerCommand.ToggleRulers: _surface.ShowRulers = !_surface.ShowRulers; break;
        }
    }
}
