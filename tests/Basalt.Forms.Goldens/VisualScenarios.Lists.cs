using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>ComboBox, ListBox, CheckedListBox, NumericUpDown and DomainUpDown.</summary>
public static partial class VisualScenarios
{
    private static readonly Size ComboSize = new(120, 22);
    private static readonly Size ListSize = new(120, 70);
    private static readonly Size UpDownSize = new(80, 22);

    private static IReadOnlyList<Scenario> ListScenarios() =>
    [
        Create<ComboBox>("combobox-dropdown", ComboSize, comboBox =>
        {
            AsEditControl(comboBox);
            comboBox.DropDownStyle = ComboBoxStyle.DropDown;
            comboBox.Items.AddRange(["One", "Two", "Three"]);
            comboBox.Text = "One";
        }, FullArea(ComboSize)),
        Create<ComboBox>("combobox-dropdownlist", ComboSize, comboBox =>
        {
            AsEditControl(comboBox);
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Items.AddRange(["One", "Two", "Three"]);
            comboBox.SelectedIndex = 0;
        }, FullArea(ComboSize)),
        Create<ComboBox>("combobox-simple", new Size(120, 70), comboBox =>
        {
            AsEditControl(comboBox);
            comboBox.DropDownStyle = ComboBoxStyle.Simple;
            comboBox.Items.AddRange(["One", "Two", "Three"]);
            comboBox.SelectedIndex = 0;
        }, FullArea(new Size(120, 70))),
        Create<ComboBox>("combobox-disabled", ComboSize, comboBox =>
        {
            AsEditControl(comboBox);
            comboBox.Items.AddRange(["One", "Two"]);
            comboBox.SelectedIndex = 0;
            comboBox.Enabled = false;
        }, FullArea(ComboSize)),

        Create<ListBox>("listbox-with-items", ListSize, listBox =>
        {
            AsEditControl(listBox);
            listBox.Items.AddRange(["Alpha", "Beta", "Gamma", "Delta"]);
        }, FullArea(ListSize)),
        Create<ListBox>("listbox-with-selection", ListSize, listBox =>
        {
            AsEditControl(listBox);
            listBox.Items.AddRange(["Alpha", "Beta", "Gamma", "Delta"]);
            listBox.SelectedIndex = 1;
        }, FullArea(ListSize)),
        Create<ListBox>("listbox-disabled", ListSize, listBox =>
        {
            AsEditControl(listBox);
            listBox.Items.AddRange(["Alpha", "Beta"]);
            listBox.Enabled = false;
        }, FullArea(ListSize)),

        Create<CheckedListBox>("checkedlistbox-mixed", ListSize, checkedListBox =>
        {
            AsEditControl(checkedListBox);
            checkedListBox.Items.Add("Unchecked");
            checkedListBox.Items.Add("Checked");
            checkedListBox.Items.Add("Indeterminate");
            checkedListBox.SetItemCheckState(1, CheckState.Checked);
            checkedListBox.SetItemCheckState(2, CheckState.Indeterminate);
        }, FullArea(ListSize)),

        Create<NumericUpDown>("numericupdown-normal", UpDownSize, numericUpDown =>
        {
            AsEditControl(numericUpDown);
            numericUpDown.Value = 42;
        }, FullArea(UpDownSize)),
        Create<NumericUpDown>("numericupdown-disabled", UpDownSize, numericUpDown =>
        {
            AsEditControl(numericUpDown);
            numericUpDown.Value = 42;
            numericUpDown.Enabled = false;
        }, FullArea(UpDownSize)),

        Create<DomainUpDown>("domainupdown-normal", UpDownSize, domainUpDown =>
        {
            AsEditControl(domainUpDown);
            domainUpDown.Items.Add("First");
            domainUpDown.Items.Add("Second");
            domainUpDown.SelectedIndex = 0;
        }, FullArea(UpDownSize)),
    ];
}
