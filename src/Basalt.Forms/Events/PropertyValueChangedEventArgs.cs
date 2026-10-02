namespace System.Windows.Forms;

public class PropertyValueChangedEventArgs : EventArgs
{
    public PropertyValueChangedEventArgs(GridItem? changedItem, object? oldValue)
    {
        ChangedItem = changedItem;
        OldValue = oldValue;
    }

    public GridItem? ChangedItem { get; }

    public object? OldValue { get; }
}
