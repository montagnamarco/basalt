using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>
/// The dialog behind PrintDialog: a printer name box, a copies/collate group
/// and a print-range group, laid out the way the Windows 2000 print dialog
/// grouped them. Every Allow* flag PrintDialog carries disables the matching
/// control here instead of hiding it, the way the native dialog greys out a
/// choice it was told not to offer.
/// </summary>
internal sealed partial class PrintDialogForm : Form
{
    private const int Edge = 11;
    private const int Gap = 8;

    private readonly ComboBox _printerComboBox;
    private readonly CheckBox _printToFileCheckBox;
    private readonly NumericUpDown _copiesUpDown;
    private readonly CheckBox _collateCheckBox;
    private readonly RadioButton _allRadioButton;
    private readonly RadioButton _selectionRadioButton;
    private readonly RadioButton _currentPageRadioButton;
    private readonly RadioButton _pagesRadioButton;
    private readonly NumericUpDown _fromPageUpDown;
    private readonly NumericUpDown _toPageUpDown;
    private readonly Button _okButton;

    public PrintDialogForm(
        PrinterSettings printerSettings,
        bool allowCurrentPage,
        bool allowPrintToFile,
        bool allowSelection,
        bool allowSomePages,
        bool printToFile,
        bool showHelp,
        bool showNetwork)
    {
        SuspendLayout();

        Text = "Print";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        var printerGroup = BuildPrinterGroup(printerSettings, allowPrintToFile, printToFile, out _printerComboBox, out _printToFileCheckBox);
        printerGroup.Location = new Point(Edge, Edge);

        var rangeGroup = BuildRangeGroup(
            printerSettings,
            allowCurrentPage,
            allowSelection,
            allowSomePages,
            out _allRadioButton,
            out _selectionRadioButton,
            out _currentPageRadioButton,
            out _pagesRadioButton,
            out _fromPageUpDown,
            out _toPageUpDown);
        rangeGroup.Location = new Point(Edge, printerGroup.Bottom + Gap);

        var copiesGroup = BuildCopiesGroup(printerSettings, out _copiesUpDown, out _collateCheckBox);
        copiesGroup.Location = new Point(rangeGroup.Right + Gap, printerGroup.Bottom + Gap);

        var buttons = BuildButtons(showHelp, showNetwork, out _okButton, out var cancelButton);
        var buttonsTop = Math.Max(rangeGroup.Bottom, copiesGroup.Bottom) + Gap;

        var clientWidth = copiesGroup.Right + Edge;
        var buttonsLeft = clientWidth - Edge - buttons.Count * 75 - (buttons.Count - 1) * 6;
        for (var i = 0; i < buttons.Count; i++) buttons[i].Location = new Point(buttonsLeft + i * (75 + 6), buttonsTop);

        Controls.Add(printerGroup);
        Controls.Add(rangeGroup);
        Controls.Add(copiesGroup);
        Controls.AddRange([.. buttons]);

        ClientSize = new Size(clientWidth, buttonsTop + 23 + Edge);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        ResumeLayout(false);
    }

    /// <summary>The checkbox state the user left "Collate" in.</summary>
    public bool Collate => _collateCheckBox.Checked;

    /// <summary>How many copies the user asked for.</summary>
    public short Copies => (short)_copiesUpDown.Value;

    /// <summary>The page the "Pages" range starts at; meaningful only when <see cref="PrintRange"/> is <see cref="PrintRange.SomePages"/>.</summary>
    public int FromPage => (int)_fromPageUpDown.Value;

    /// <summary>The printer name the user left in the combo box, typed or picked.</summary>
    public string PrinterName => _printerComboBox.Text;

    /// <summary>Whether "Print to file" is checked.</summary>
    public bool PrintToFile => _printToFileCheckBox.Checked;

    /// <summary>Which of the four range radio buttons is checked.</summary>
    public PrintRange PrintRange =>
        _selectionRadioButton.Checked ? PrintRange.Selection
        : _currentPageRadioButton.Checked ? PrintRange.CurrentPage
        : _pagesRadioButton.Checked ? PrintRange.SomePages
        : PrintRange.AllPages;

    /// <summary>The page the "Pages" range ends at; meaningful only when <see cref="PrintRange"/> is <see cref="PrintRange.SomePages"/>.</summary>
    public int ToPage => (int)_toPageUpDown.Value;
}
