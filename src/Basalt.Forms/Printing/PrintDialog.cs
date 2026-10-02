using System.ComponentModel;
using System.Drawing.Printing;
using System.Windows.Forms.Internal.Dialogs;

namespace System.Windows.Forms;

/// <summary>
/// The standard print dialog: a printer picker, a copies/collate box and a
/// page-range choice, drawn from our own controls rather than the native
/// Windows PrintDlg. There is no real printer to enumerate on this platform
/// (see <see cref="PrinterSettings.InstalledPrinters"/>), so the printer list
/// only ever offers the name <see cref="PrinterSettings"/> already carries.
/// </summary>
[DefaultProperty("Document")]
public sealed class PrintDialog : CommonDialog
{
    private PrinterSettings? _printerSettings;

    public PrintDialog()
    {
    }

    [DefaultValue(false)]
    public bool AllowCurrentPage { get; set; }

    [DefaultValue(true)]
    public bool AllowPrintToFile { get; set; } = true;

    [DefaultValue(false)]
    public bool AllowSelection { get; set; }

    [DefaultValue(false)]
    public bool AllowSomePages { get; set; }

    [DefaultValue(null)]
    public PrintDocument? Document { get; set; }

    [DefaultValue(false)]
    public bool PrintToFile { get; set; }

    /// <summary>
    /// Mirrors <see cref="Document"/>'s own <see cref="PrintDocument.PrinterSettings"/>
    /// while a document is attached, since that is what printing would
    /// actually use; otherwise this dialog's own settings, created on first
    /// use like the native dialog's default CHOOSEPRINTER state.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PrinterSettings PrinterSettings
    {
        get => Document?.PrinterSettings ?? (_printerSettings ??= new PrinterSettings());
        set => _printerSettings = value ?? throw new ArgumentNullException(nameof(value));
    }

    [DefaultValue(false)]
    public bool ShowHelp { get; set; }

    [DefaultValue(true)]
    public bool ShowNetwork { get; set; } = true;

    [DefaultValue(false)]
    public bool UseEXDialog { get; set; }

    public override void Reset()
    {
        AllowCurrentPage = false;
        AllowPrintToFile = true;
        AllowSelection = false;
        AllowSomePages = false;
        Document = null;
        PrintToFile = false;
        ShowHelp = false;
        ShowNetwork = true;
        UseEXDialog = false;
        _printerSettings = null;
    }

    protected override bool RunDialog(IntPtr hwndOwner)
    {
        var settings = PrinterSettings;

        using var form = new PrintDialogForm(
            settings,
            AllowCurrentPage,
            AllowPrintToFile,
            AllowSelection,
            AllowSomePages,
            PrintToFile,
            ShowHelp,
            ShowNetwork);

        form.HelpRequested += (_, _) => OnHelpRequest(EventArgs.Empty);

        var result = form.ShowDialog(OwnerForm());
        if (result != DialogResult.OK) return false;

        settings.PrinterName = form.PrinterName;
        settings.Copies = form.Copies;
        settings.Collate = form.Collate;
        settings.PrintRange = form.PrintRange;
        settings.FromPage = form.FromPage;
        settings.ToPage = form.ToPage;
        settings.PrintToFile = form.PrintToFile;
        PrintToFile = form.PrintToFile;

        return true;
    }

    /// <summary>
    /// The owner ShowDialog(IWin32Window) was given, found back from the host
    /// window ResolveOwnerHost already resolved: CommonDialog keeps the
    /// original owner private, but every open Form can be matched by its own
    /// host window. Mirrors ColorDialog's own helper.
    /// </summary>
    private Form? OwnerForm()
    {
        var host = ResolveOwnerHost();
        return host is null ? null : Application.OpenForms.Snapshot().FirstOrDefault(form => form.Host == host);
    }
}
