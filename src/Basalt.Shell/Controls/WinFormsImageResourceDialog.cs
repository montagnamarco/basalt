using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>
/// Visual Studio's "Select Resource" dialog for an Image, BackgroundImage,
/// InitialImage, ErrorImage or Icon property.
/// </summary>
/// <remarks>
/// "Local resource" reads an image file's bytes, which the caller then writes into the
/// form's own .resx the way Visual Studio does. "Project resource file" lists the images
/// already in the project's My Project\Resources.resx (both ResXFileRef and embedded
/// entries, through <see cref="WinFormsProjectResources"/>) and lets Import… add a new one
/// there; it is enabled only when the caller can name a .vbproj for the form being edited
/// (<paramref name="projectPath"/> below), the same way Visual Studio disables it for a
/// form with no project.
/// </remarks>
internal sealed class WinFormsImageResourceDialog : Window
{
    private readonly Image _preview = new() { Width = 220, Height = 180, Stretch = Stretch.Uniform };
    private readonly TextBlock _error = new()
    {
        Name = "WinFormsImageDialogError",
        Foreground = Brushes.Firebrick,
        IsVisible = false,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };
    private readonly Func<Task<string?>> _importFile;
    private readonly bool _isIcon;
    private readonly string? _projectPath;
    private readonly RadioButton _projectRadio;
    private readonly ListBox _projectList = new() { Name = "WinFormsImageDialogProjectList", Height = 100 };
    private readonly List<WinFormsProjectResourceEntry> _projectEntries = [];

    public byte[]? SelectedBytes { get; private set; }

    /// <summary>Whether the user asked to remove the image rather than replace it.</summary>
    public bool Cleared { get; private set; }

    /// <summary>
    /// Whether the user actually imported a file: false when OK is pressed without
    /// touching Import… or Clear, so the caller can leave an unmodified image alone
    /// rather than rewrite it with a freshly re-encoded (and not byte-identical) copy.
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>Whether OK was pressed with "Project resource file" selected and an entry picked.</summary>
    public bool UsedProjectResource { get; private set; }

    /// <summary>The project resource name chosen, valid only when <see cref="UsedProjectResource"/> is true.</summary>
    public string? SelectedProjectResourceName { get; private set; }

    /// <param name="currentBytes">The bytes already assigned, shown as the starting preview.</param>
    /// <param name="isIcon">
    /// Whether the edited property is an Icon rather than an Image/Bitmap: Icon only
    /// accepts .ico (an <c>IconConverter</c> cannot read a PNG back at startup), and an
    /// imported file is validated by actually decoding it as the target type.
    /// </param>
    /// <param name="importFile">
    /// Opens a file picker and returns the chosen path, or null if cancelled.
    /// Overridable so tests can supply a path without a real OS dialog.
    /// </param>
    /// <param name="projectPath">
    /// The .vbproj the edited form belongs to, or null when it could not be found (no
    /// .vbproj above the form, or a .csproj instead): "Project resource file" is disabled.
    /// </param>
    public WinFormsImageResourceDialog(byte[]? currentBytes, bool isIcon, Func<Task<string?>>? importFile = null,
        string? projectPath = null)
    {
        _isIcon = isIcon;
        _importFile = importFile ?? (() => PickFileAsync(this, isIcon));
        _projectPath = projectPath;
        SelectedBytes = currentBytes;
        Title = Localizer.Get(StringKeys.WinFormsImageDialogTitle);
        Width = 480;
        Height = 480;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var localRadio = new RadioButton
        {
            Content = Localizer.Get(StringKeys.WinFormsImageDialogLocalResource),
            GroupName = "WinFormsImageResourceSource",
            IsChecked = true,
        };
        _projectRadio = new RadioButton
        {
            Content = Localizer.Get(StringKeys.WinFormsImageDialogProjectResource),
            GroupName = "WinFormsImageResourceSource",
            // Browsing and importing need a project to hold Resources.resx; see the remarks above.
            IsEnabled = _projectPath is not null,
        };

        var import = new Button { Content = Localizer.Get(StringKeys.WinFormsImageDialogImport), Name = "WinFormsImageDialogImport" };
        var clear = new Button { Content = Localizer.Get(StringKeys.WinFormsImageDialogClear), Name = "WinFormsImageDialogClear" };
        import.Click += async (_, _) => await ImportAsync();
        clear.Click += (_, _) =>
        {
            SelectedBytes = null;
            Cleared = true;
            _error.IsVisible = false;
            ShowPreview(SelectedBytes);
        };

        var projectImport = new Button
        {
            Content = Localizer.Get(StringKeys.WinFormsImageDialogImport),
            Name = "WinFormsImageDialogProjectImport",
        };
        projectImport.Click += async (_, _) => await ImportProjectAsync();

        var ok = new Button { Content = Localizer.Get(StringKeys.WinFormsEditorOk), IsDefault = true };
        var cancel = new Button { Content = Localizer.Get(StringKeys.WinFormsEditorCancel), IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (_projectRadio.IsChecked == true && _projectList.SelectedItem is WinFormsProjectResourceEntry selected)
            {
                UsedProjectResource = true;
                SelectedProjectResourceName = selected.Name;
            }
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, cancel },
        };
        var localRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(16, 0, 0, 0),
            Children = { import, clear },
        };
        var projectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(16, 0, 0, 0),
            Children = { projectImport },
        };
        _projectList.Margin = new Thickness(16, 0, 0, 0);
        _projectList.SelectionChanged += (_, _) =>
        {
            if (_projectList.SelectedItem is WinFormsProjectResourceEntry selected) ShowPreview(selected.Bytes);
        };

        void UpdateMode()
        {
            var useProject = _projectRadio.IsChecked == true;
            localRow.IsVisible = !useProject;
            _projectList.IsVisible = useProject;
            projectRow.IsVisible = useProject;
            ShowPreview(useProject
                ? (_projectList.SelectedItem as WinFormsProjectResourceEntry)?.Bytes
                : SelectedBytes);
        }
        localRadio.IsCheckedChanged += (_, _) => UpdateMode();
        _projectRadio.IsCheckedChanged += (_, _) => UpdateMode();

        if (_projectPath is not null)
        {
            _projectEntries.AddRange(WinFormsProjectResources.ListEntries(_projectPath));
            _projectList.ItemsSource = _projectEntries;
        }

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = Spacing.Normal,
            Children = { localRadio, localRow, _projectRadio, projectRow, _projectList, _preview, _error, buttons },
        };
        Content = layout;
        UpdateMode();
    }

    private async Task ImportAsync()
    {
        try
        {
            var path = await _importFile();
            if (path is null) return;
            var bytes = await File.ReadAllBytesAsync(path);
            if (!TryDecode(bytes, _isIcon, out var decodeError))
            {
                _error.Text = Localizer.Get(StringKeys.WinFormsImageDialogInvalidFormat, decodeError!);
                _error.IsVisible = true;
                return;
            }

            SelectedBytes = bytes;
            Cleared = false;
            Changed = true;
            _error.IsVisible = false;
            ShowPreview(SelectedBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _error.Text = Localizer.Get(StringKeys.WinFormsImageDialogImportFailed, exception.Message);
            _error.IsVisible = true;
        }
    }

    /// <summary>
    /// Imports a file into the project's My Project\Resources.resx the way "Project resource
    /// file" > Import… does in Visual Studio: validated the same way a Local resource import
    /// is, then added to the project through <see cref="WinFormsProjectResources.Import"/>
    /// and selected, so the newly imported entry is what OK picks up.
    /// </summary>
    private async Task ImportProjectAsync()
    {
        if (_projectPath is null) return;
        try
        {
            var path = await _importFile();
            if (path is null) return;
            var bytes = await File.ReadAllBytesAsync(path);
            if (!TryDecode(bytes, _isIcon, out var decodeError))
            {
                _error.Text = Localizer.Get(StringKeys.WinFormsImageDialogInvalidFormat, decodeError!);
                _error.IsVisible = true;
                return;
            }

            // File IO off the UI thread: Import reads and rewrites Resources.resx,
            // Resources.Designer.vb and the .vbproj, then copies the file in, none of
            // which the dialog's own thread should block on.
            var projectPath = _projectPath;
            var entry = await Task.Run(() => WinFormsProjectResources.Import(projectPath, path, _isIcon));
            _projectEntries.Add(entry);
            _projectList.ItemsSource = null;
            _projectList.ItemsSource = _projectEntries;
            _projectList.SelectedItem = entry;
            _error.IsVisible = false;
            ShowPreview(entry.Bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            // FormatException covers a Resources.resx or Resources.Designer.vb that Import
            // could not find its way around (see WinFormsProjectResources.Import): nothing
            // was written in that case, so showing the error here and leaving the project
            // list untouched is the whole recovery, not just the message.
            _error.Text = Localizer.Get(StringKeys.WinFormsImageDialogProjectImportFailed, exception.Message);
            _error.IsVisible = true;
        }
    }

    /// <summary>
    /// Whether the compiled app will actually be able to read these bytes back: an
    /// Icon property is validated with <c>System.Drawing.Icon</c>'s own stream
    /// constructor, an Image/Bitmap property with <c>Image.FromStream</c>, the same
    /// calls <c>IconConverter</c> and <c>ImageConverter</c> make from
    /// InitializeComponent. Both names resolve to Basalt.Drawing, the cross-platform
    /// System.Drawing.Common this designer (and the compiled app) actually runs on.
    /// </summary>
    private static bool TryDecode(byte[] bytes, bool isIcon, out string? error)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            if (isIcon)
            {
                using var icon = new System.Drawing.Icon(stream);
            }
            else
            {
                using var image = System.Drawing.Image.FromStream(stream);
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    private void ShowPreview(byte[]? bytes)
    {
        _preview.Source = null;
        if (bytes is not { Length: > 0 }) return;
        try
        {
            using var stream = new MemoryStream(bytes);
            _preview.Source = new Bitmap(stream);
        }
        catch (Exception)
        {
            // The preview is a convenience; an unreadable file still round-trips
            // its bytes into the resx exactly as imported.
        }
    }

    private static async Task<string?> PickFileAsync(Window owner, bool isIcon)
    {
        // An Icon property only decodes a .ico (System.Drawing.Icon's own format);
        // offering .ico for Image/BackgroundImage is left out rather than wired, since
        // accepting it would need converting to a Bitmap the way Visual Studio does,
        // which this dialog does not do.
        var patterns = isIcon
            ? new[] { "*.ico" }
            : new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp" };
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get(StringKeys.WinFormsImageDialogImport),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get(StringKeys.WinFormsImageDialogImageFiles))
                {
                    Patterns = patterns,
                },
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
