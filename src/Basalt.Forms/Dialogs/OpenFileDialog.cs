using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>Prompts the user to pick one or more existing files, through Avalonia's open file picker.</summary>
public sealed class OpenFileDialog : FileDialog
{
    public OpenFileDialog()
    {
        CheckFileExists = true;
        SelectReadOnly = true;
    }

    [DefaultValue(true)]
    public override bool CheckFileExists
    {
        get => base.CheckFileExists;
        set => base.CheckFileExists = value;
    }

    [DefaultValue(false)]
    public bool Multiselect { get; set; }

    /// <summary>
    /// Whether the "Open as read-only" checkbox was checked. Avalonia's open
    /// picker has no such checkbox, so this stays at whatever the caller sets
    /// it to: there is no native UI element driving it here.
    /// </summary>
    [DefaultValue(false)]
    public bool ReadOnlyChecked { get; set; }

    [Browsable(false)]
    public string SafeFileName => Path.GetFileName(FileName);

    [Browsable(false)]
    public string[] SafeFileNames => [.. FileNames.Select(name => Path.GetFileName(name)!)];

    [DefaultValue(true)]
    public bool SelectReadOnly { get; set; } = true;

    /// <summary>A native preview pane is not part of Avalonia's picker; stored for API compatibility only.</summary>
    [DefaultValue(false)]
    public bool ShowPreview { get; set; }

    /// <summary>Whether a read-only checkbox is shown; Avalonia's open picker has none, so this has no visible effect.</summary>
    [DefaultValue(false)]
    public bool ShowReadOnly { get; set; }

    /// <summary>Opens the first selected file for reading, as WinForms does.</summary>
    public Stream OpenFile()
    {
        if (FileName.Length == 0) throw new ArgumentException("No file name was selected.");

        return new FileStream(FileName, FileMode.Open, FileAccess.Read);
    }

    public override void Reset()
    {
        base.Reset();
        CheckFileExists = true;
        Multiselect = false;
        ReadOnlyChecked = false;
        SelectReadOnly = true;
        ShowPreview = false;
        ShowReadOnly = false;
    }

    private protected override bool AllowsMultipleSelection => Multiselect;

    private protected override string[] RunFileDialog(HostWindow? owner, FilePickerRequest request) =>
        FilePickers.Current.PickOpenFiles(owner, request);
}
