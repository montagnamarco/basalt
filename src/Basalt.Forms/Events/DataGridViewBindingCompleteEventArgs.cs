using System.ComponentModel;

namespace System.Windows.Forms;

public class DataGridViewBindingCompleteEventArgs : EventArgs
{
    public DataGridViewBindingCompleteEventArgs(ListChangedType listChangedType)
    {
        ListChangedType = listChangedType;
    }

    public ListChangedType ListChangedType { get; }
}
