using System.ComponentModel.Design;
using System.Windows.Forms.ComponentModel.Com2Interop;
using Microsoft.Win32;

namespace System.Windows.Forms;

public partial class PropertyGrid : IComPropertyBrowser
{
    private bool _inPropertySet;

    bool IComPropertyBrowser.InPropertySet => _inPropertySet;
    private event ComponentRenameEventHandler? ComComponentNameChanged;
    event ComponentRenameEventHandler? IComPropertyBrowser.ComComponentNameChanged
    {
        add => ComComponentNameChanged += value;
        remove => ComComponentNameChanged -= value;
    }

    void IComPropertyBrowser.DropDownDone() => Focus();

    bool IComPropertyBrowser.EnsurePendingChangesCommitted()
    {
        if (_editingEntry is not { } entry) return true;
        if (!TryConvertFromText(entry.PropertyDescriptor!, _editor!.Text, out var value)) return false;
        if (!CommitValue(entry, value)) return false;
        EndEdit();
        return true;
    }

    void IComPropertyBrowser.HandleF4()
    {
        if (SelectedGridItem is not PropertyGridEntry entry) return;
        if (TryCycleStandardValue(entry)) return;
        var rows = VisibleRowsWithDepth();
        var index = rows.FindIndex(row => ReferenceEquals(row.Item, entry));
        if (index >= 0) BeginEdit(index, entry);
    }

    void IComPropertyBrowser.LoadState(RegistryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Registry property grid state requires Windows.");
        if (key.GetValue(nameof(PropertySort)) is int sort && Enum.IsDefined(typeof(PropertySort), sort)) PropertySort = (PropertySort)sort;
        if (key.GetValue(nameof(HelpVisible)) is int help) HelpVisible = help != 0;
        if (key.GetValue(nameof(ToolbarVisible)) is int toolbar) ToolbarVisible = toolbar != 0;
        if (key.GetValue(nameof(LargeButtons)) is int large) LargeButtons = large != 0;
        if (key.GetValue("LabelColumnWidth") is int width && width >= 0) _labelColumnWidth = width;
        PerformLayout();
        Invalidate();
    }

    void IComPropertyBrowser.SaveState(RegistryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Registry property grid state requires Windows.");
        key.SetValue(nameof(PropertySort), (int)PropertySort);
        key.SetValue(nameof(HelpVisible), HelpVisible ? 1 : 0);
        key.SetValue(nameof(ToolbarVisible), ToolbarVisible ? 1 : 0);
        key.SetValue(nameof(LargeButtons), LargeButtons ? 1 : 0);
        key.SetValue("LabelColumnWidth", _labelColumnWidth);
    }
}
