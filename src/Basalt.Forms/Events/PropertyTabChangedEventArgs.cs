using System.Windows.Forms.Design;

namespace System.Windows.Forms;

public class PropertyTabChangedEventArgs : EventArgs
{
    public PropertyTabChangedEventArgs(PropertyTab? oldTab, PropertyTab? newTab)
    {
        OldTab = oldTab;
        NewTab = newTab;
    }

    public PropertyTab? NewTab { get; }

    public PropertyTab? OldTab { get; }
}
