using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>The three group boxes and the button row PageSetupDialogForm's constructor lays out.</summary>
internal sealed partial class PageSetupDialogForm
{
    private GroupBox BuildPaperGroup(PageSettings pageSettings, bool allowPaper, out ComboBox paperSizeComboBox)
    {
        var sizeLabel = new Label { AutoSize = true, Text = "Size:", Location = new Point(8, 20) };

        paperSizeComboBox = new ComboBox
        {
            Name = "PaperSizeComboBox",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = allowPaper,
            Location = new Point(sizeLabel.Right + Gap, 17),
            Width = 160,
        };

        foreach (var paperSize in _paperSizes) paperSizeComboBox.Items.Add(paperSize.PaperName);

        var selectedIndex = Array.FindIndex(_paperSizes, size => size.PaperName == pageSettings.PaperSize.PaperName);
        paperSizeComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        var group = new GroupBox { Text = "Paper", Size = new Size(250, paperSizeComboBox.Bottom + Gap) };
        group.Controls.Add(sizeLabel);
        group.Controls.Add(paperSizeComboBox);
        return group;
    }

    private static GroupBox BuildOrientationGroup(
        PageSettings pageSettings, bool allowOrientation, out RadioButton portraitRadioButton, out RadioButton landscapeRadioButton)
    {
        portraitRadioButton = new RadioButton
        {
            Name = "PortraitRadioButton", Text = "Portrait", AutoSize = true, Enabled = allowOrientation, Location = new Point(8, 18),
        };
        landscapeRadioButton = new RadioButton
        {
            Name = "LandscapeRadioButton",
            Text = "Landscape",
            AutoSize = true,
            Enabled = allowOrientation,
            Location = new Point(8, portraitRadioButton.Bottom + 4),
        };

        if (pageSettings.Landscape) landscapeRadioButton.Checked = true;
        else portraitRadioButton.Checked = true;

        var group = new GroupBox { Text = "Orientation", Size = new Size(120, landscapeRadioButton.Bottom + Gap) };
        group.Controls.Add(portraitRadioButton);
        group.Controls.Add(landscapeRadioButton);
        return group;
    }

    private static GroupBox BuildMarginsGroup(
        PageSettings pageSettings,
        bool allowMargins,
        bool enableMetric,
        Margins? minMargins,
        out NumericUpDown leftMarginUpDown,
        out NumericUpDown rightMarginUpDown,
        out NumericUpDown topMarginUpDown,
        out NumericUpDown bottomMarginUpDown)
    {
        var margins = pageSettings.Margins;
        var floor = minMargins ?? new Margins(0, 0, 0, 0);

        var group = new GroupBox { Text = enableMetric ? "Margins (millimeters)" : "Margins (inches)" };

        leftMarginUpDown = MarginField(group, "LeftMarginUpDown", "Left:", 8, 18, margins.Left, floor.Left, allowMargins, enableMetric);
        rightMarginUpDown = MarginField(group, "RightMarginUpDown", "Right:", 8, leftMarginUpDown.Bottom + Gap, margins.Right, floor.Right, allowMargins, enableMetric);
        topMarginUpDown = MarginField(group, "TopMarginUpDown", "Top:", 120, 18, margins.Top, floor.Top, allowMargins, enableMetric);
        bottomMarginUpDown = MarginField(group, "BottomMarginUpDown", "Bottom:", 120, leftMarginUpDown.Bottom + Gap, margins.Bottom, floor.Bottom, allowMargins, enableMetric);

        group.Size = new Size(230, rightMarginUpDown.Bottom + Gap);
        return group;
    }

    /// <summary>One "Left:"-style label and its spin box, added to the group, the value converted to inches or millimeters for display and clamped to its own minimum margin.</summary>
    private static NumericUpDown MarginField(
        GroupBox parent, string name, string caption, int left, int top, int currentHundredthsInch, int minimumHundredthsInch, bool enabled, bool enableMetric)
    {
        var label = new Label { AutoSize = true, Text = caption, Location = new Point(left, top + 3) };
        parent.Controls.Add(label);

        var upDown = new NumericUpDown
        {
            Name = name,
            Enabled = enabled,
            DecimalPlaces = enableMetric ? 1 : 2,
            Increment = enableMetric ? 0.5m : 0.1m,
            Minimum = ToDisplay(minimumHundredthsInch, enableMetric),
            Maximum = ToDisplay(2000, enableMetric),
            Location = new Point(label.Right + 4, top),
            Width = 60,
        };
        parent.Controls.Add(upDown);

        upDown.Value = Math.Clamp(ToDisplay(currentHundredthsInch, enableMetric), upDown.Minimum, upDown.Maximum);
        return upDown;
    }

    /// <summary>Hundredths of an inch, GDI+'s own printer unit, to the decimal the spin box shows: millimeters, or inches.</summary>
    private static decimal ToDisplay(int hundredthsOfAnInch, bool enableMetric) =>
        enableMetric
            ? Math.Round((decimal)PrinterUnitConvert.Convert(hundredthsOfAnInch, PrinterUnit.Display, PrinterUnit.TenthsOfAMillimeter) / 10m, 1)
            : Math.Round(hundredthsOfAnInch / 100m, 2);

    /// <summary>The reverse of <see cref="ToDisplay"/>: back to hundredths of an inch for a Margins object.</summary>
    private static int FromDisplay(decimal displayValue, bool enableMetric) =>
        enableMetric
            ? PrinterUnitConvert.Convert((int)Math.Round(displayValue * 10m), PrinterUnit.TenthsOfAMillimeter, PrinterUnit.Display)
            : (int)Math.Round(displayValue * 100m);

    private List<Button> BuildButtons(bool allowPrinter, bool showHelp, bool showNetwork, out Button okButton, out Button cancelButton)
    {
        okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(75, 23) };
        cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 23) };

        var buttons = new List<Button> { okButton, cancelButton };

        // There is no printer-selection sub-dialog on this platform; the button is shown for layout fidelity but never does anything.
        buttons.Add(new Button { Name = "PrinterButton", Text = "Printer...", Enabled = allowPrinter, Size = new Size(75, 23) });

        if (showNetwork)
        {
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
