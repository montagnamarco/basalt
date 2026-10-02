using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class PrintPreviewDialog
{
    /// <summary>The smallest a user can size the dialog to: small, but still usable, as Microsoft's own default.</summary>
    protected override Size DefaultMinimumSize => new(240, 160);

    protected override void CreateHandle() => base.CreateHandle();

    /// <summary>
    /// Nothing here runs a preview generation on a background thread (it runs
    /// inline, inside <see cref="PrintPreviewControl"/>'s paint), so there is
    /// none to cancel; the override exists only to keep the public shape
    /// Microsoft declares on this type.
    /// </summary>
#pragma warning disable CS0672 // Form.OnClosing is obsolete, but it is still the one Windows Forms calls before closing, and Microsoft overrides it here too without marking the override obsolete.
    protected override void OnClosing(CancelEventArgs e)
    {
#pragma warning disable CS0618
        base.OnClosing(e);
#pragma warning restore CS0618
    }
#pragma warning restore CS0672

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessTabKey(bool forward) => base.ProcessTabKey(forward);
}
