using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>
/// The dialog behind PageSetupDialog: a paper size box, an orientation choice
/// and the four margins, laid out the way the Windows 2000 page setup dialog
/// grouped them. Every Allow* flag PageSetupDialog carries disables the
/// matching control here instead of hiding it, the way the native dialog
/// greys out a choice it was told not to offer.
/// </summary>
internal sealed partial class PageSetupDialogForm : Form
{
    private const int Edge = 11;
    private const int Gap = 8;

    private readonly PaperSize[] _paperSizes;
    private readonly bool _enableMetric;

    private readonly ComboBox _paperSizeComboBox;
    private readonly RadioButton _portraitRadioButton;
    private readonly RadioButton _landscapeRadioButton;
    private readonly NumericUpDown _leftMarginUpDown;
    private readonly NumericUpDown _rightMarginUpDown;
    private readonly NumericUpDown _topMarginUpDown;
    private readonly NumericUpDown _bottomMarginUpDown;
    private readonly Button _okButton;

    public PageSetupDialogForm(
        PageSettings pageSettings,
        PrinterSettings? printerSettings,
        bool allowMargins,
        bool allowOrientation,
        bool allowPaper,
        bool allowPrinter,
        bool enableMetric,
        Margins? minMargins,
        bool showHelp,
        bool showNetwork)
    {
        _enableMetric = enableMetric;
        _paperSizes = (printerSettings?.PaperSizes.Cast<PaperSize>() ?? [pageSettings.PaperSize]).ToArray();

        SuspendLayout();

        Text = "Page Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        var paperGroup = BuildPaperGroup(pageSettings, allowPaper, out _paperSizeComboBox);
        paperGroup.Location = new Point(Edge, Edge);

        var orientationGroup = BuildOrientationGroup(pageSettings, allowOrientation, out _portraitRadioButton, out _landscapeRadioButton);
        orientationGroup.Location = new Point(Edge, paperGroup.Bottom + Gap);

        var marginsGroup = BuildMarginsGroup(
            pageSettings,
            allowMargins,
            enableMetric,
            minMargins,
            out _leftMarginUpDown,
            out _rightMarginUpDown,
            out _topMarginUpDown,
            out _bottomMarginUpDown);
        marginsGroup.Location = new Point(orientationGroup.Right + Gap, paperGroup.Bottom + Gap);

        var buttons = BuildButtons(allowPrinter, showHelp, showNetwork, out _okButton, out var cancelButton);
        var buttonsTop = Math.Max(orientationGroup.Bottom, marginsGroup.Bottom) + Gap;

        var clientWidth = marginsGroup.Right + Edge;
        var buttonsLeft = clientWidth - Edge - buttons.Count * 75 - (buttons.Count - 1) * 6;
        for (var i = 0; i < buttons.Count; i++) buttons[i].Location = new Point(buttonsLeft + i * (75 + 6), buttonsTop);

        Controls.Add(paperGroup);
        Controls.Add(orientationGroup);
        Controls.Add(marginsGroup);
        Controls.AddRange([.. buttons]);

        ClientSize = new Size(clientWidth, buttonsTop + 23 + Edge);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        ResumeLayout(false);
    }

    /// <summary>Whether the user left "Landscape" checked.</summary>
    public bool Landscape => _landscapeRadioButton.Checked;

    /// <summary>The four margins the user left the spin boxes at, converted back to hundredths of an inch.</summary>
    public Margins Margins => new(
        FromDisplay(_leftMarginUpDown.Value, _enableMetric),
        FromDisplay(_rightMarginUpDown.Value, _enableMetric),
        FromDisplay(_topMarginUpDown.Value, _enableMetric),
        FromDisplay(_bottomMarginUpDown.Value, _enableMetric));

    /// <summary>The paper size the user left selected in the combo box.</summary>
    public PaperSize PaperSize => _paperSizes[Math.Max(_paperSizeComboBox.SelectedIndex, 0)];
}
