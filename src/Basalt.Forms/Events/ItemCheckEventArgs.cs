namespace System.Windows.Forms;

/// <summary>
/// A check box is about to change in a <see cref="CheckedListBox"/> or <see cref="ListView"/>: the
/// value it is about to take, which a handler may still replace, and the
/// value it currently has.
/// </summary>
public class ItemCheckEventArgs : EventArgs
{
    public ItemCheckEventArgs(int index, CheckState newCheckValue, CheckState currentValue)
    {
        Index = index;
        NewValue = newCheckValue;
        CurrentValue = currentValue;
    }

    public CheckState CurrentValue { get; }

    public int Index { get; }

    public CheckState NewValue { get; set; }
}
