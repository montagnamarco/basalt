using System.ComponentModel;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// The site of a control the user's project defines, while the designer previews it. Its design mode
/// is what lets Basalt.Forms contain a paint failure of the control and draw the error frame instead of
/// throwing through the surface; the designer's sink is the one service the control can ask for.
/// </summary>
/// <remarks>
/// Only project controls get this site, never a built-in preview control, so a built-in control's failure
/// is not reported as the project's code. There is no container: the preview has no IContainer that owns
/// its controls, and the session disposes the form and its controls itself.
/// </remarks>
internal sealed class WinFormsPreviewSite(IComponent component, string name, Forms.IDesignTimePaintFailureSink paintFailureSink) : ISite
{
    public IComponent Component { get; } = component;

    public IContainer? Container => null;

    public bool DesignMode => true;

    public string? Name { get; set; } = name;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(Forms.IDesignTimePaintFailureSink) ? paintFailureSink : null;
}
