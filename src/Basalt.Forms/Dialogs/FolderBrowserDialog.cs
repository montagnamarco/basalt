using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>Prompts the user to pick a folder, through Avalonia's folder picker.</summary>
[DefaultEvent("HelpRequest")]
[DefaultProperty("SelectedPath")]
public sealed class FolderBrowserDialog : CommonDialog
{
    private string _selectedPath = string.Empty;
    private string[] _selectedPaths = [];
    private string _initialDirectory = string.Empty;
    private string _description = string.Empty;

    public new event EventHandler? HelpRequest
    {
        add => base.HelpRequest += value;
        remove => base.HelpRequest -= value;
    }

    [DefaultValue(true)]
    public bool AddToRecent { get; set; } = true;

    [DefaultValue(true)]
    public bool AutoUpgradeEnabled { get; set; } = true;

    [DefaultValue(null)]
    public Guid? ClientGuid { get; set; }

    [DefaultValue("")]
    public string Description
    {
        get => _description;
        set => _description = value ?? string.Empty;
    }

    [DefaultValue("")]
    public string InitialDirectory
    {
        get => _initialDirectory;
        set => _initialDirectory = value ?? string.Empty;
    }

    [DefaultValue(false)]
    public bool Multiselect { get; set; }

    [DefaultValue(false)]
    public bool OkRequiresInteraction { get; set; }

    /// <summary>Used as a fallback start location when InitialDirectory is empty; Avalonia has no special-folder picker argument, so this is resolved to a path first.</summary>
    [DefaultValue(Environment.SpecialFolder.Desktop)]
    public Environment.SpecialFolder RootFolder { get; set; } = Environment.SpecialFolder.Desktop;

    [DefaultValue("")]
    public string SelectedPath
    {
        get => _selectedPath;
        set => _selectedPath = value ?? string.Empty;
    }

    [Browsable(false)]
    public string[] SelectedPaths => [.. _selectedPaths];

    [DefaultValue(false)]
    public bool ShowHiddenFiles { get; set; }

    /// <summary>Avalonia's folder picker always lets the user create a folder; there is no toggle to hide that button.</summary>
    [DefaultValue(true)]
    public bool ShowNewFolderButton { get; set; } = true;

    [DefaultValue(true)]
    public bool ShowPinnedPlaces { get; set; } = true;

    [DefaultValue(false)]
    public bool UseDescriptionForTitle { get; set; }

    public override void Reset()
    {
        _selectedPath = string.Empty;
        _selectedPaths = [];
        _initialDirectory = string.Empty;
        _description = string.Empty;
        AddToRecent = true;
        AutoUpgradeEnabled = true;
        ClientGuid = null;
        Multiselect = false;
        OkRequiresInteraction = false;
        RootFolder = Environment.SpecialFolder.Desktop;
        ShowHiddenFiles = false;
        ShowNewFolderButton = true;
        ShowPinnedPlaces = true;
        UseDescriptionForTitle = false;
    }

    protected override bool RunDialog(IntPtr hwndOwner)
    {
        var owner = ResolveOwnerHost();

        var startDirectory = InitialDirectory.Length > 0
            ? InitialDirectory
            : SelectedPath.Length > 0 ? SelectedPath : ResolveRootFolder();

        var title = UseDescriptionForTitle ? Description : string.Empty;

        var request = new FolderPickerRequest(title, startDirectory, Multiselect);
        var selected = FilePickers.Current.PickFolders(owner, request);

        if (selected.Length == 0) return false;

        _selectedPaths = selected;
        _selectedPath = selected[0];

        return true;
    }

    private string? ResolveRootFolder()
    {
        var path = Environment.GetFolderPath(RootFolder);

        return path.Length == 0 ? null : path;
    }
}
