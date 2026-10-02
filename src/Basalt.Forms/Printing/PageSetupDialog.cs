using System.ComponentModel;
using System.Drawing.Printing;
using System.Windows.Forms.Internal.Dialogs;

namespace System.Windows.Forms;

/// <summary>
/// The standard page setup dialog: paper size, orientation and the four
/// margins, drawn from our own controls rather than the native Windows
/// PAGESETUPDLG. There is no real printer to enumerate on this platform (see
/// <see cref="PrinterSettings.InstalledPrinters"/>), so the paper sizes on
/// offer are whatever <see cref="PrinterSettings.PaperSizes"/> reports.
/// </summary>
[DefaultProperty("Document")]
public sealed class PageSetupDialog : CommonDialog
{
    private PageSettings? _pageSettings;
    private PrinterSettings? _printerSettings;

    public PageSetupDialog()
    {
    }

    [DefaultValue(true)]
    public bool AllowMargins { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowOrientation { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowPaper { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowPrinter { get; set; } = true;

    [DefaultValue(null)]
    public PrintDocument? Document { get; set; }

    [DefaultValue(false)]
    public bool EnableMetric { get; set; }

    public Margins? MinMargins { get; set; }

    /// <summary>
    /// Mirrors <see cref="Document"/>'s own <see cref="PrintDocument.DefaultPageSettings"/>
    /// while a document is attached, since that is what printing would
    /// actually use; otherwise this dialog's own page settings, created on
    /// first use like the native dialog's default PAGESETUPDLG state.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PageSettings? PageSettings
    {
        get => Document?.DefaultPageSettings ?? (_pageSettings ??= new PageSettings());
        set => _pageSettings = value;
    }

    /// <summary>
    /// An explicitly assigned printer, else the one <see cref="PageSettings"/>
    /// already carries.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PrinterSettings? PrinterSettings
    {
        get => _printerSettings ?? PageSettings?.PrinterSettings;
        set => _printerSettings = value;
    }

    [DefaultValue(false)]
    public bool ShowHelp { get; set; }

    [DefaultValue(true)]
    public bool ShowNetwork { get; set; } = true;

    public override void Reset()
    {
        AllowMargins = true;
        AllowOrientation = true;
        AllowPaper = true;
        AllowPrinter = true;
        Document = null;
        EnableMetric = false;
        MinMargins = null;
        ShowHelp = false;
        ShowNetwork = true;
        _pageSettings = null;
        _printerSettings = null;
    }

    protected override bool RunDialog(IntPtr hwndOwner)
    {
        // PageSettings never returns null on its own (it lazily creates a
        // default), but the declared surface is nullable because a caller
        // can set it back to null; a running dialog still needs something to edit.
        var settings = PageSettings ?? new PageSettings();
        var printerSettings = PrinterSettings ?? settings.PrinterSettings;

        using var form = new PageSetupDialogForm(
            settings,
            printerSettings,
            AllowMargins,
            AllowOrientation,
            AllowPaper,
            AllowPrinter,
            EnableMetric,
            MinMargins,
            ShowHelp,
            ShowNetwork);

        form.HelpRequested += (_, _) => OnHelpRequest(EventArgs.Empty);

        var result = form.ShowDialog(OwnerForm());
        if (result != DialogResult.OK) return false;

        // settings is the very object PageSettings already returns (Document's
        // own, or this dialog's lazily created one), so mutating it in place
        // is enough: no separate write-back assignment is needed.
        settings.PaperSize = form.PaperSize;
        settings.Landscape = form.Landscape;
        settings.Margins = form.Margins;

        return true;
    }

    /// <summary>
    /// The owner ShowDialog(IWin32Window) was given, found back from the host
    /// window ResolveOwnerHost already resolved. Mirrors ColorDialog's own helper.
    /// </summary>
    private Form? OwnerForm()
    {
        var host = ResolveOwnerHost();
        return host is null ? null : Application.OpenForms.Snapshot().FirstOrDefault(form => form.Host == host);
    }
}
