using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>Button, CheckBox and RadioButton: the push button and the two check controls.</summary>
public static partial class VisualScenarios
{
    private static readonly Size ButtonSize = new(100, 28);

    private static IReadOnlyList<Scenario> ButtonScenarios() =>
    [
        Create<Button>("button-normal-no-text", ButtonSize, _ => { }),
        Create<Button>("button-normal-with-text", ButtonSize, button => button.Text = "OK", FullArea(ButtonSize)),
        Create<Button>("button-default-border", ButtonSize, button =>
        {
            button.Text = "OK";
            button.NotifyDefault(true);
        }, FullArea(ButtonSize)),
        Create<Button>("button-disabled", ButtonSize, button =>
        {
            button.Text = "OK";
            button.Enabled = false;
        }, FullArea(ButtonSize)),
        Create<Button>("button-flatstyle-flat", ButtonSize, button =>
        {
            button.Text = "Flat";
            button.FlatStyle = FlatStyle.Flat;
        }, FullArea(ButtonSize)),
        Create<Button>("button-flatstyle-popup", ButtonSize, button =>
        {
            button.Text = "Popup";
            button.FlatStyle = FlatStyle.Popup;
        }, FullArea(ButtonSize)),
        Create<Button>("button-flatstyle-standard", ButtonSize, button =>
        {
            button.Text = "Standard";
            button.FlatStyle = FlatStyle.Standard;
        }, FullArea(ButtonSize)),
    ];

    private static readonly Size CheckSize = new(120, 24);

    private static IReadOnlyList<Scenario> CheckScenarios() =>
    [
        Create<CheckBox>("checkbox-unchecked", CheckSize, checkBox => checkBox.Text = "Check", FullArea(CheckSize)),
        Create<CheckBox>("checkbox-checked", CheckSize, checkBox =>
        {
            checkBox.Text = "Check";
            checkBox.Checked = true;
        }, FullArea(CheckSize)),
        Create<CheckBox>("checkbox-indeterminate", CheckSize, checkBox =>
        {
            checkBox.Text = "Check";
            checkBox.CheckState = CheckState.Indeterminate;
        }, FullArea(CheckSize)),
        Create<CheckBox>("checkbox-disabled", CheckSize, checkBox =>
        {
            checkBox.Text = "Check";
            checkBox.Enabled = false;
        }, FullArea(CheckSize)),
        Create<CheckBox>("checkbox-appearance-button-checked", CheckSize, checkBox =>
        {
            checkBox.Text = "Check";
            checkBox.Appearance = Appearance.Button;
            checkBox.Checked = true;
        }, FullArea(CheckSize)),

        Create<RadioButton>("radiobutton-unchecked", CheckSize, radioButton => radioButton.Text = "Option", FullArea(CheckSize)),
        Create<RadioButton>("radiobutton-checked", CheckSize, radioButton =>
        {
            radioButton.Text = "Option";
            radioButton.Checked = true;
        }, FullArea(CheckSize)),
        Create<RadioButton>("radiobutton-disabled", CheckSize, radioButton =>
        {
            radioButton.Text = "Option";
            radioButton.Enabled = false;
        }, FullArea(CheckSize)),
        Create<RadioButton>("radiobutton-appearance-button-checked", CheckSize, radioButton =>
        {
            radioButton.Text = "Option";
            radioButton.Appearance = Appearance.Button;
            radioButton.Checked = true;
        }, FullArea(CheckSize)),
    ];
}
