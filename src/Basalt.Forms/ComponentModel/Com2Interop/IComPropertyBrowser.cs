using System.ComponentModel.Design;
using Microsoft.Win32;

namespace System.Windows.Forms.ComponentModel.Com2Interop;

public interface IComPropertyBrowser
{
    bool InPropertySet { get; }

    event ComponentRenameEventHandler? ComComponentNameChanged;

    void DropDownDone();

    bool EnsurePendingChangesCommitted();

    void HandleF4();

    void LoadState(RegistryKey key);

    void SaveState(RegistryKey key);
}
