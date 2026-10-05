using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task SetItemsAsync(Forms.Control control, IReadOnlyList<string> values)
    {
        if (control is not (Forms.ListBox or Forms.ComboBox)) throw new ArgumentException("The control does not have an editable item collection.");
        var name = NameOf(control);
        var snapshot = values.ToArray();
        return ApplyCollectionAsync(document => document.Collections.SetItems(name, snapshot));
    }

    public Task SetColumnsAsync(Forms.Control control, IReadOnlyList<WinFormsColumnDefinition> values)
    {
        if (control is not Forms.DataGridView) throw new ArgumentException("The control does not have an editable grid column collection.");
        var name = NameOf(control);
        var snapshot = values.ToArray();
        return ApplyCollectionAsync(document => document.Collections.SetColumns(name, snapshot));
    }
}
