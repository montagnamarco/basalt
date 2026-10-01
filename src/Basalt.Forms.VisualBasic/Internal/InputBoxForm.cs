using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.VisualBasic.Internal;

/// <summary>
/// The dialog behind InputBox: a prompt, a single-line text box pre-filled
/// with the default response, and OK/Cancel, laid out the way the Windows
/// 2000 common dialogs lay out a prompt and an edit box.
/// </summary>
internal sealed class InputBoxForm : Form
{
    private const int Edge = 11;
    private const int PromptWidth = 260;
    private const int ButtonWidth = 75;
    private const int ButtonHeight = 23;
    private const int ButtonGap = 6;

    // One twip is 1/1440 inch; at the 96 dots per inch Basalt.Forms assumes
    // everywhere else, that is 1/15 of a pixel.
    private const double TwipsPerPixel = 15.0;

    private readonly TextBox _response;

    public InputBoxForm(string prompt, string title, string defaultResponse, int xPos, int yPos)
    {
        SuspendLayout();

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        var label = new Label
        {
            AutoSize = false,
            Text = prompt,
            UseMnemonic = false,
        };

        var promptSize = TextRenderer.MeasureText(prompt, Font, new Size(PromptWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        label.Bounds = new Rectangle(Edge, Edge, PromptWidth, promptSize.Height);

        _response = new TextBox { Text = defaultResponse };
        var textTop = Edge + promptSize.Height + 8;
        _response.Location = new Point(Edge, textTop);
        _response.Width = PromptWidth;

        var buttonsTop = textTop + _response.Height + Edge;
        var clientWidth = PromptWidth + 2 * Edge;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(ButtonWidth, ButtonHeight) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(ButtonWidth, ButtonHeight) };
        ok.Location = new Point(clientWidth - Edge - 2 * ButtonWidth - ButtonGap, buttonsTop);
        cancel.Location = new Point(clientWidth - Edge - ButtonWidth, buttonsTop);

        ClientSize = new Size(clientWidth, buttonsTop + ButtonHeight + Edge);

        Controls.Add(label);
        Controls.Add(_response);
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _response;

        StartPosition = xPos < 0 && yPos < 0 ? FormStartPosition.CenterScreen : FormStartPosition.Manual;
        if (StartPosition == FormStartPosition.Manual) Location = PositionFrom(xPos, yPos);

        ResumeLayout(false);
    }

    /// <summary>What the user typed; "" is read back by the caller only when Cancel was pressed.</summary>
    public string ResponseText => _response.Text;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _response.SelectAll();
        _response.Focus();
    }

    /// <summary>
    /// XPos/YPos are twips from the screen's top-left corner; -1 in either one
    /// means VB's own default for that axis: centered horizontally, or a
    /// third of the way down the primary screen.
    /// </summary>
    private Point PositionFrom(int xPos, int yPos)
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);

        var x = xPos >= 0 ? screen.Left + (int)(xPos / TwipsPerPixel) : screen.Left + (screen.Width - Width) / 2;
        var y = yPos >= 0 ? screen.Top + (int)(yPos / TwipsPerPixel) : screen.Top + screen.Height / 3;

        return new Point(x, y);
    }
}
