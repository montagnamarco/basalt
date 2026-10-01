namespace System.Windows.Forms;

/// <summary>
/// Which row of a CurrencyManager's list changed, or -1 when every row did.
/// </summary>
/// <remarks>
/// Belongs with the other generated event types in Events/, but that folder
/// is owned by another family's exclusive file list (only BindingComplete*.cs
/// is ours); kept here, next to the one class that raises it, until the
/// owner moves it.
/// </remarks>
public class ItemChangedEventArgs : EventArgs
{
    // Microsoft's type has no public constructor either: only a CurrencyManager raises it.
    internal ItemChangedEventArgs(int index)
    {
        Index = index;
    }

    public int Index { get; }
}
