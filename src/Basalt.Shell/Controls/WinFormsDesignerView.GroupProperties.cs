using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private void ShowGroupProperties(IReadOnlyList<Forms.Control> controls)
    {
        var session = _session!;
        var selected = controls.ToArray();
        _selectedName = session.NameOf(selected[^1]);
        _tray.Select(null);
        _layoutToolbar.SelectedCount = selected.Length;
        _properties.ShowProperties(session.CommonProperties(selected),
            (property, value) => RunEditAsync(() => session.SetGroupPropertyAsync(selected, property, value)));
        // A shared Handles edit has no single event receiver or handler name.
        _events.Show(null);
    }
}
