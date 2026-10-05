namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private void InitializeZOrderActions()
    {
        _surface.ZOrderRequested += bringToFront =>
        {
            if (_session is not { } session)
            {
                return;
            }
            var selected = _surface.SelectedControls.ToArray();
            RunEditAsync(() => session.SetZOrderAsync(selected, bringToFront));
        };
    }
}
