using Avalonia;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    private void ShowGuides(FormsControl parent, WinFormsDesignerSnap.SnapResult result)
    {
        var bounds = BoundsOnHost(parent);
        var client = new Rect(bounds.X + parent.NonClient.Left,
            bounds.Y + parent.NonClient.Top,
            parent.ClientSize.Width, parent.ClientSize.Height);
        _guides.Show(client,
            result.VerticalGuide is { } x ? client.X + x : null,
            result.HorizontalGuide is { } y ? client.Y + y : null);
    }
}
