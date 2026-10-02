using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>The three group boxes and the button row PrintDialogForm's constructor lays out.</summary>
internal sealed partial class PrintDialogForm
{
    private static GroupBox BuildPrinterGroup(
        PrinterSettings printerSettings, bool allowPrintToFile, bool printToFile, out ComboBox printerComboBox, out CheckBox printToFileCheckBox)
    {
        var nameLabel = new Label { AutoSize = true, Text = "Name:", Location = new Point(8, 20) };

        printerComboBox = new ComboBox
        {
            Name = "PrinterComboBox",
            DropDownStyle = ComboBoxStyle.DropDown,
            Location = new Point(nameLabel.Right + Gap, 17),
            Width = 220,
        };

        foreach (var installed in PrinterSettings.InstalledPrinters)
        {
            printerComboBox.Items.Add(installed);
        }

        if (printerSettings.PrinterName.Length > 0 && !printerComboBox.Items.Contains(printerSettings.PrinterName))
        {
            printerComboBox.Items.Add(printerSettings.PrinterName);
        }

        printerComboBox.Text = printerSettings.PrinterName;

        printToFileCheckBox = new CheckBox
        {
            Name = "PrintToFileCheckBox",
            Text = "Print to file",
            AutoSize = true,
            Checked = printToFile,
            Enabled = allowPrintToFile,
            Location = new Point(8, printerComboBox.Bottom + Gap),
        };

        var group = new GroupBox { Text = "Printer", Size = new Size(320, printToFileCheckBox.Bottom + Gap) };
        group.Controls.Add(nameLabel);
        group.Controls.Add(printerComboBox);
        group.Controls.Add(printToFileCheckBox);
        return group;
    }

    private static GroupBox BuildRangeGroup(
        PrinterSettings printerSettings,
        bool allowCurrentPage,
        bool allowSelection,
        bool allowSomePages,
        out RadioButton allRadioButton,
        out RadioButton selectionRadioButton,
        out RadioButton currentPageRadioButton,
        out RadioButton pagesRadioButton,
        out NumericUpDown fromPageUpDown,
        out NumericUpDown toPageUpDown)
    {
        allRadioButton = new RadioButton { Name = "AllRadioButton", Text = "All", AutoSize = true, Location = new Point(8, 18) };
        selectionRadioButton = new RadioButton
        {
            Name = "SelectionRadioButton", Text = "Selection", AutoSize = true, Enabled = allowSelection, Location = new Point(8, allRadioButton.Bottom + 4),
        };
        currentPageRadioButton = new RadioButton
        {
            Name = "CurrentPageRadioButton",
            Text = "Current Page",
            AutoSize = true,
            Enabled = allowCurrentPage,
            Location = new Point(8, selectionRadioButton.Bottom + 4),
        };
        pagesRadioButton = new RadioButton
        {
            Name = "PagesRadioButton", Text = "Pages", AutoSize = true, Enabled = allowSomePages, Location = new Point(8, currentPageRadioButton.Bottom + 4),
        };

        var fromLabel = new Label { AutoSize = true, Text = "from:", Location = new Point(24, pagesRadioButton.Bottom + 8) };
        var minimumPage = Math.Max(printerSettings.MinimumPage, 1);
        var maximumPage = Math.Max(printerSettings.MaximumPage, minimumPage);

        fromPageUpDown = new NumericUpDown
        {
            Name = "FromPageUpDown",
            Minimum = minimumPage,
            Maximum = maximumPage,
            Enabled = false,
            Location = new Point(fromLabel.Right + 4, fromLabel.Top - 3),
            Width = 45,
        };
        fromPageUpDown.Value = printerSettings.FromPage <= 0 ? minimumPage : Math.Clamp(printerSettings.FromPage, minimumPage, maximumPage);

        var toLabel = new Label { AutoSize = true, Text = "to:", Location = new Point(fromPageUpDown.Right + Gap, fromLabel.Top) };

        toPageUpDown = new NumericUpDown
        {
            Name = "ToPageUpDown",
            Minimum = minimumPage,
            Maximum = maximumPage,
            Enabled = false,
            Location = new Point(toLabel.Right + 4, fromLabel.Top - 3),
            Width = 45,
        };
        toPageUpDown.Value = printerSettings.ToPage <= 0 ? maximumPage : Math.Clamp(printerSettings.ToPage, minimumPage, maximumPage);

        var fromPageUpDownLocal = fromPageUpDown;
        var toPageUpDownLocal = toPageUpDown;

        // A range never runs backwards: raising "from" past "to" carries "to" along, and the other way round.
        if (fromPageUpDown.Value > toPageUpDown.Value) toPageUpDown.Value = fromPageUpDown.Value;
        fromPageUpDown.ValueChanged += (_, _) =>
        {
            if (fromPageUpDownLocal.Value > toPageUpDownLocal.Value) toPageUpDownLocal.Value = fromPageUpDownLocal.Value;
        };
        toPageUpDown.ValueChanged += (_, _) =>
        {
            if (toPageUpDownLocal.Value < fromPageUpDownLocal.Value) fromPageUpDownLocal.Value = toPageUpDownLocal.Value;
        };
        var pagesRadioButtonLocal = pagesRadioButton;
        void UpdatePageFieldsEnabled(object? sender, EventArgs e)
        {
            var enabled = pagesRadioButtonLocal.Checked;
            fromPageUpDownLocal.Enabled = enabled;
            toPageUpDownLocal.Enabled = enabled;
        }

        allRadioButton.CheckedChanged += UpdatePageFieldsEnabled;
        selectionRadioButton.CheckedChanged += UpdatePageFieldsEnabled;
        currentPageRadioButton.CheckedChanged += UpdatePageFieldsEnabled;
        pagesRadioButton.CheckedChanged += UpdatePageFieldsEnabled;

        SelectInitialRange(printerSettings.PrintRange, allowSelection, allowCurrentPage, allowSomePages, allRadioButton, selectionRadioButton, currentPageRadioButton, pagesRadioButton);

        var group = new GroupBox { Text = "Print range", Size = new Size(150, toPageUpDown.Bottom + Gap) };
        group.Controls.Add(allRadioButton);
        group.Controls.Add(selectionRadioButton);
        group.Controls.Add(currentPageRadioButton);
        group.Controls.Add(pagesRadioButton);
        group.Controls.Add(fromLabel);
        group.Controls.Add(fromPageUpDown);
        group.Controls.Add(toLabel);
        group.Controls.Add(toPageUpDown);
        return group;
    }

    /// <summary>Checks the radio button that matches the printer's current PrintRange, falling back to "All" when that range is not allowed.</summary>
    private static void SelectInitialRange(
        PrintRange printRange,
        bool allowSelection,
        bool allowCurrentPage,
        bool allowSomePages,
        RadioButton allRadioButton,
        RadioButton selectionRadioButton,
        RadioButton currentPageRadioButton,
        RadioButton pagesRadioButton)
    {
        if (printRange == PrintRange.Selection && allowSelection) selectionRadioButton.Checked = true;
        else if (printRange == PrintRange.CurrentPage && allowCurrentPage) currentPageRadioButton.Checked = true;
        else if (printRange == PrintRange.SomePages && allowSomePages) pagesRadioButton.Checked = true;
        else allRadioButton.Checked = true;
    }

    private static GroupBox BuildCopiesGroup(PrinterSettings printerSettings, out NumericUpDown copiesUpDown, out CheckBox collateCheckBox)
    {
        var copiesLabel = new Label { AutoSize = true, Text = "Number of copies:", Location = new Point(8, 18) };

        copiesUpDown = new NumericUpDown
        {
            Name = "CopiesUpDown",
            Minimum = 1,
            Maximum = printerSettings.MaximumCopies,
            Value = Math.Clamp((int)printerSettings.Copies, 1, printerSettings.MaximumCopies),
            Location = new Point(8, copiesLabel.Bottom + 4),
            Width = 60,
        };

        collateCheckBox = new CheckBox
        {
            Name = "CollateCheckBox",
            Text = "Collate",
            AutoSize = true,
            Checked = printerSettings.Collate,
            Location = new Point(8, copiesUpDown.Bottom + Gap),
        };

        var group = new GroupBox { Text = "Copies", Size = new Size(140, collateCheckBox.Bottom + Gap) };
        group.Controls.Add(copiesLabel);
        group.Controls.Add(copiesUpDown);
        group.Controls.Add(collateCheckBox);
        return group;
    }

    private List<Button> BuildButtons(bool showHelp, bool showNetwork, out Button okButton, out Button cancelButton)
    {
        okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(75, 23) };
        cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 23) };

        var buttons = new List<Button> { okButton, cancelButton };

        if (showNetwork)
        {
            // There is no network to browse on this platform; the button is shown for layout fidelity but never does anything.
            buttons.Add(new Button { Name = "NetworkButton", Text = "Network...", Enabled = false, Size = new Size(75, 23) });
        }

        if (showHelp)
        {
            var helpButton = new Button { Text = "Help", Size = new Size(75, 23) };
            helpButton.Click += (_, _) => RaiseHelpRequested(new HelpEventArgs(MousePosition));
            buttons.Add(helpButton);
        }

        return buttons;
    }
}
