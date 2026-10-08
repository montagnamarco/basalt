using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private async Task PlaceControlAsync(WinFormsControlDrop drop)
    {
        var session = _session!;
        var names = session.Document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A toolbox item that is not itself a Control (Timer, OpenFileDialog, ...) has
        // no location to drop onto - AddTool below takes the same branch for a
        // double-click on the very same catalogue entry.
        if (typeof(Forms.Control).IsAssignableFrom(drop.ControlType))
        {
            var parentName = session.NameOf(drop.Parent);
            await session.AddControlAtAsync(drop.ControlType, parentName, drop.Location);
        }
        else
        {
            await session.AddComponentAsync(drop.ControlType);
        }

        var added = session.Document.DeclaredNames.FirstOrDefault(name => !names.Contains(name));
        if (added is not null && session.FindComponent(added) is { } component) ShowComponentProperties(component);
        else if (added is not null && session.FindControl(added) is { } control) _surface.Select(control);
    }
}
