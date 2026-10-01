using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>Prompts the user to pick a path to save to, through Avalonia's save file picker.</summary>
public sealed class SaveFileDialog : FileDialog
{
    public SaveFileDialog()
    {
        OverwritePrompt = true;
        ExpandedMode = true;
        CheckWriteAccess = true;
    }

    [DefaultValue(true)]
    public bool CheckWriteAccess { get; set; } = true;

    /// <summary>
    /// Whether to ask before using a name that does not exist yet. A save
    /// picker lets the user type any name by nature, and Avalonia's picker
    /// has no such extra confirmation, so this is stored but not enforced.
    /// </summary>
    [DefaultValue(false)]
    public bool CreatePrompt { get; set; }

    [DefaultValue(true)]
    public bool ExpandedMode { get; set; } = true;

    /// <summary>Forwarded to the platform picker (FilePickerSaveOptions.ShowOverwritePrompt): the native picker owns the confirmation itself.</summary>
    [DefaultValue(true)]
    public bool OverwritePrompt { get; set; } = true;

    /// <summary>Opens the chosen path for writing, creating or truncating it, as WinForms does.</summary>
    public Stream OpenFile()
    {
        if (FileName.Length == 0) throw new ArgumentException("No file name was selected.");

        return new FileStream(FileName, FileMode.Create, FileAccess.ReadWrite);
    }

    public override void Reset()
    {
        base.Reset();
        CheckWriteAccess = true;
        CreatePrompt = false;
        ExpandedMode = true;
        OverwritePrompt = true;
    }

    private protected override bool ShowOverwritePromptCore => OverwritePrompt;

    private protected override string[] RunFileDialog(HostWindow? owner, FilePickerRequest request)
    {
        var path = FilePickers.Current.PickSaveFile(owner, request);

        return path is null ? [] : [path];
    }
}
