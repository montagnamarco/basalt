using System.Drawing;
using System.Globalization;
using System.Resources;

namespace System.Windows.Forms;

public class ThreadExceptionDialog : Form
{
    private static readonly ResourceManager s_resources = new("System.Windows.Forms.ThreadExceptionDialog", typeof(ThreadExceptionDialog).Assembly);

    public ThreadExceptionDialog(Exception t)
    {
        ArgumentNullException.ThrowIfNull(t);
        Text = s_resources.GetString("Caption", CultureInfo.CurrentUICulture) ?? string.Empty;
        ClientSize = new Size(580, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var message = new Label { Text = t.Message, Location = new Point(12, 12), Size = new Size(556, 48) };
        var details = new TextBox
        {
            Text = t.ToString(), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Location = new Point(12, 68), Size = new Size(556, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        var dismiss = new Button
        {
            Text = s_resources.GetString("Dismiss", CultureInfo.CurrentUICulture) ?? string.Empty,
            DialogResult = DialogResult.OK, Location = new Point(468, 320), Size = new Size(100, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        Controls.AddRange([message, details, dismiss]);
        AcceptButton = dismiss;
        CancelButton = dismiss;
    }

    public override bool AutoSize { get => base.AutoSize; set => base.AutoSize = value; }
    public new event EventHandler? AutoSizeChanged { add => base.AutoSizeChanged += value; remove => base.AutoSizeChanged -= value; }
}
