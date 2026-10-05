namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private async Task PlaceControlAsync(WinFormsControlDrop drop)
    {
        var session = _session!;
        var parentName = session.NameOf(drop.Parent);
        var names = session.Document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await session.AddControlAtAsync(drop.ControlType, parentName, drop.Location);
        var added = session.Document.Controls.Single(control => !names.Contains(control.Name));
        _surface.Select(session.FindControl(added.Name));
    }
}
