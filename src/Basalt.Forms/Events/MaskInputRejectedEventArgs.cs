using System.ComponentModel;

namespace System.Windows.Forms;

public class MaskInputRejectedEventArgs : EventArgs
{
    public MaskInputRejectedEventArgs(int position, MaskedTextResultHint rejectionHint)
    {
        Position = position;
        RejectionHint = rejectionHint;
    }

    public int Position { get; }

    public MaskedTextResultHint RejectionHint { get; }
}
