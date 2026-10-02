namespace System.Windows.Forms;

public class SelectedGridItemChangedEventArgs : EventArgs
{
    public SelectedGridItemChangedEventArgs(GridItem? oldSel, GridItem? newSel)
    {
        OldSelection = oldSel;
        NewSelection = newSel;
    }

    public GridItem? NewSelection { get; }

    public GridItem? OldSelection { get; }
}
