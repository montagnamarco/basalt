using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>TextBox, MaskedTextBox and RichTextBox: the edit controls, all on a white edit background.</summary>
public static partial class VisualScenarios
{
    private static readonly Size EditSize = new(120, 22);
    private static readonly Size MultilineEditSize = new(120, 60);

    /// <summary>Every edit control starts on the white background real Windows gives an unset one.</summary>
    private static void AsEditControl(Control control)
    {
        control.BackColor = SystemColors.Window;
        control.ForeColor = SystemColors.WindowText;
    }

    private static IReadOnlyList<Scenario> TextScenarios() =>
    [
        Create<TextBox>("textbox-empty", EditSize, AsEditControl),
        Create<TextBox>("textbox-with-text", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.Text = "Text";
        }, FullArea(EditSize)),
        Create<TextBox>("textbox-multiline", MultilineEditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.Multiline = true;
            textBox.Text = "Line one\r\nLine two";
        }, FullArea(MultilineEditSize)),
        Create<TextBox>("textbox-readonly", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.ReadOnly = true;
            textBox.Text = "Read only";
        }, FullArea(EditSize)),
        Create<TextBox>("textbox-disabled", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.Text = "Disabled";
            textBox.Enabled = false;
        }, FullArea(EditSize)),
        Create<TextBox>("textbox-borderstyle-none", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.BorderStyle = BorderStyle.None;
        }),
        Create<TextBox>("textbox-borderstyle-fixedsingle", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }),
        Create<TextBox>("textbox-borderstyle-fixed3d", EditSize, textBox =>
        {
            AsEditControl(textBox);
            textBox.BorderStyle = BorderStyle.Fixed3D;
        }),

        Create<MaskedTextBox>("maskedtextbox-empty-mask", EditSize, maskedTextBox =>
        {
            AsEditControl(maskedTextBox);
            maskedTextBox.Mask = "00/00/0000";
        }, FullArea(EditSize)),
        Create<MaskedTextBox>("maskedtextbox-filled-mask", EditSize, maskedTextBox =>
        {
            AsEditControl(maskedTextBox);
            maskedTextBox.Mask = "00/00/0000";
            maskedTextBox.Text = "01011999";
        }, FullArea(EditSize)),

        Create<RichTextBox>("richtextbox-plain", MultilineEditSize, richTextBox =>
        {
            AsEditControl(richTextBox);
            richTextBox.Text = "Plain rich text";
        }, FullArea(MultilineEditSize)),
    ];
}
