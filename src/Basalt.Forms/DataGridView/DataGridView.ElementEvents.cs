namespace System.Windows.Forms;

public partial class DataGridView
{
    internal void RaiseElementDataErrorCore(DataGridViewDataErrorEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnDataError(displayErrorDialogIfNoHandler: false, e);
    }

    internal void RaiseElementMouseWheelCore(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnMouseWheel(e);
    }
}
