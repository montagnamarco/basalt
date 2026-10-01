using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Base of OpenFileDialog and SaveFileDialog: parses Filter, resolves the
/// platform picker through FilePickers.Current and applies the WinForms
/// validation rules (AddExtension, CheckFileExists, CheckPathExists, FileOk)
/// to whatever path the picker returned.
/// </summary>
[DefaultEvent("FileOk")]
[DefaultProperty("FileName")]
public abstract class FileDialog : CommonDialog
{
    protected static readonly object EventFileOk = new();

    /// <summary>
    /// Private protected: Microsoft's own FileDialog has no public or
    /// protected constructor either, so no outside assembly can derive a
    /// third file dialog kind. OpenFileDialog and SaveFileDialog, in this
    /// assembly, can still call it.
    /// </summary>
    private protected FileDialog()
    {
    }

    private string _fileName = string.Empty;
    private string[] _fileNames = [];
    private string _filter = string.Empty;
    private IReadOnlyList<FilterEntry> _parsedFilter = [];
    private string _initialDirectory = string.Empty;
    private string _title = string.Empty;
    private string _defaultExt = string.Empty;

    [DefaultValue(true)]
    public bool AddExtension { get; set; } = true;

    [DefaultValue(true)]
    public bool AddToRecent { get; set; } = true;

    [DefaultValue(true)]
    public bool AutoUpgradeEnabled { get; set; } = true;

    [DefaultValue(false)]
    public virtual bool CheckFileExists { get; set; }

    [DefaultValue(true)]
    public bool CheckPathExists { get; set; } = true;

    [DefaultValue(null)]
    public Guid? ClientGuid { get; set; }

    [DefaultValue("")]
    public string DefaultExt
    {
        get => _defaultExt;
        set => _defaultExt = (value ?? string.Empty).TrimStart('.');
    }

    [DefaultValue(true)]
    public bool DereferenceLinks { get; set; } = true;

    [DefaultValue("")]
    public string FileName
    {
        get => _fileName;
        set => _fileName = value ?? string.Empty;
    }

    [Browsable(false)]
    public string[] FileNames => [.. _fileNames];

    [DefaultValue("")]
    public string Filter
    {
        get => _filter;
        set
        {
            _parsedFilter = ParseFilter(value);
            _filter = value ?? string.Empty;
        }
    }

    [DefaultValue(1)]
    public int FilterIndex { get; set; } = 1;

    [DefaultValue("")]
    public string InitialDirectory
    {
        get => _initialDirectory;
        set => _initialDirectory = value ?? string.Empty;
    }

    [DefaultValue(false)]
    public bool OkRequiresInteraction { get; set; }

    [DefaultValue(false)]
    public bool RestoreDirectory { get; set; }

    [DefaultValue(false)]
    public bool ShowHelp { get; set; }

    [DefaultValue(false)]
    public bool ShowHiddenFiles { get; set; }

    [DefaultValue(true)]
    public bool ShowPinnedPlaces { get; set; } = true;

    [DefaultValue(false)]
    public bool SupportMultiDottedExtensions { get; set; }

    [DefaultValue("")]
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    [DefaultValue(true)]
    public bool ValidateNames { get; set; } = true;

    public event CancelEventHandler? FileOk;

    protected void OnFileOk(CancelEventArgs e) => FileOk?.Invoke(this, e);

    public override void Reset()
    {
        _fileName = string.Empty;
        _fileNames = [];
        Filter = string.Empty;
        FilterIndex = 1;
        _initialDirectory = string.Empty;
        _title = string.Empty;
        _defaultExt = string.Empty;
        AddExtension = true;
        AddToRecent = true;
        AutoUpgradeEnabled = true;
        CheckFileExists = false;
        CheckPathExists = true;
        ClientGuid = null;
        DereferenceLinks = true;
        OkRequiresInteraction = false;
        RestoreDirectory = false;
        ShowHelp = false;
        ShowHiddenFiles = false;
        ShowPinnedPlaces = true;
        SupportMultiDottedExtensions = false;
        ValidateNames = true;
    }

    public override string ToString() => $"{base.ToString()}: FileName: {FileName}";

    /// <summary>
    /// Asks the concrete picker for a selection, applies AddExtension, then
    /// CheckFileExists/CheckPathExists, then raises FileOk. WinForms reopens
    /// the native dialog when a handler cancels FileOk or validation fails;
    /// the platform picker here has already closed by that point, so this
    /// ends the dialog as Cancel instead of looping back into it.
    /// </summary>
    protected override bool RunDialog(IntPtr hwndOwner)
    {
        var owner = ResolveOwnerHost();
        var previousDirectory = RestoreDirectory ? Environment.CurrentDirectory : null;

        try
        {
            var request = BuildRequest();
            var selected = RunFileDialog(owner, request);
            if (selected.Length == 0) return false;

            selected = ApplyDefaultExtension(selected);
            if (!ValidateSelection(selected)) return false;

            _fileNames = selected;
            _fileName = selected[0];

            var cancelArgs = new CancelEventArgs(false);
            OnFileOk(cancelArgs);

            return !cancelArgs.Cancel;
        }
        finally
        {
            if (previousDirectory is not null) Environment.CurrentDirectory = previousDirectory;
        }
    }

    /// <summary>Runs the concrete picker (open, possibly multiple files, or save) and returns the chosen paths.</summary>
    private protected abstract string[] RunFileDialog(HostWindow? owner, FilePickerRequest request);

    /// <summary>Whether the concrete dialog lets the platform picker select more than one path.</summary>
    private protected virtual bool AllowsMultipleSelection => false;

    /// <summary>
    /// Whether the platform picker itself should warn before overwriting an
    /// existing file (SaveFileDialog.OverwritePrompt): the native picker owns
    /// that confirmation, so this only forwards the flag.
    /// </summary>
    private protected virtual bool ShowOverwritePromptCore => false;

    private FilePickerRequest BuildRequest() => new(
        Title,
        InitialDirectory.Length == 0 ? null : InitialDirectory,
        FileName.Length == 0 ? null : FileName,
        AllowsMultipleSelection,
        [.. _parsedFilter.Select(entry => new FilePickerFilter(entry.Description, entry.Patterns))],
        FilterIndex,
        ShowOverwritePromptCore);

    /// <summary>Appends DefaultExt to a path with no extension, as AddExtension asks.</summary>
    /// <summary>
    /// A name typed without an extension gets one, as WinForms adds it: the
    /// DefaultExt first, then the extensions of the chosen filter in order; a
    /// wildcard extension ("*.*") leaves the name as typed. When the file must
    /// exist, the first candidate that exists wins.
    /// </summary>
    private string[] ApplyDefaultExtension(string[] paths)
    {
        if (!AddExtension) return paths;

        var extensions = CandidateExtensions();
        if (extensions.Count == 0) return paths;

        var result = new string[paths.Length];

        for (var i = 0; i < paths.Length; i++)
        {
            result[i] = paths[i];
            if (Path.GetExtension(paths[i]).Length != 0) continue;

            foreach (var extension in extensions)
            {
                var candidate = extension.IndexOfAny(['*', '?']) >= 0 ? paths[i] : $"{paths[i]}.{extension}";
                if (!CheckFileExists || File.Exists(candidate))
                {
                    result[i] = candidate;
                    break;
                }
            }
        }

        return result;
    }

    private List<string> CandidateExtensions()
    {
        var extensions = new List<string>();
        if (DefaultExt.Length > 0) extensions.Add(DefaultExt);

        if (FilterIndex >= 1 && FilterIndex <= _parsedFilter.Count)
        {
            foreach (var pattern in _parsedFilter[FilterIndex - 1].Patterns)
            {
                var dot = pattern.LastIndexOf('.');
                if (dot >= 0 && dot + 1 < pattern.Length) extensions.Add(pattern[(dot + 1)..]);
            }
        }

        return extensions;
    }

    private bool ValidateSelection(string[] paths)
    {
        foreach (var path in paths)
        {
            if (CheckFileExists && !File.Exists(path)) return false;

            if (CheckPathExists)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// "Text files|*.txt|All files|*.*" parses to description/pattern pairs;
    /// each pattern side can list several patterns separated by ';'. An odd
    /// number of '|'-separated segments is the same mistake WinForms rejects.
    /// </summary>
    private static IReadOnlyList<FilterEntry> ParseFilter(string? filter)
    {
        if (string.IsNullOrEmpty(filter)) return [];

        var segments = filter.Split('|');

        if (segments.Length % 2 != 0)
        {
            throw new ArgumentException(
                "The provided filter string is invalid. The filter string must contain a description of the filter, followed by the vertical bar character (|) and the filter pattern. The strings for different filtering options must also be separated by the vertical bar character. Example: \"Text Files (*.txt)|*.txt|All files (*.*)|*.*\"",
                nameof(filter));
        }

        var entries = new List<FilterEntry>(segments.Length / 2);

        for (var i = 0; i < segments.Length; i += 2)
        {
            var patterns = segments[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            entries.Add(new FilterEntry(segments[i], patterns));
        }

        return entries;
    }

    private readonly record struct FilterEntry(string Description, string[] Patterns);
}
