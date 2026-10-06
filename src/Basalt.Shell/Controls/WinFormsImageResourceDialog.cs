using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>
/// Visual Studio's "Select Resource" dialog for an Image, BackgroundImage,
/// InitialImage, ErrorImage or Icon property.
/// </summary>
/// <remarks>
/// Only the "Local resource" choice is wired to a project file today: Import…
/// reads an image file's bytes, which the caller then writes into the form's
/// .resx the way Visual Studio does. "Project resource file" (My Project's
/// Resources.resx, listing existing entries and adding new ones through the
/// resource generator) is shown but disabled; it needs the project model this
/// dialog does not have access to, and is left for a follow-up rather than
/// wired incompletely.
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

    public byte[]? SelectedBytes { get; private set; }

    /// <summary>Whether the user asked to remove the image rather than replace it.</summary>
    public bool Cleared { get; private set; }

    /// <summary>
    /// Whether the user actually imported a file: false when OK is pressed without
    /// touching Import… or Clear, so the caller can leave an unmodified image alone
    /// rather than rewrite it with a freshly re-encoded (and not byte-identical) copy.
    /// </summary>
    public bool Changed { get; private set; }

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
    public WinFormsImageResourceDialog(byte[]? currentBytes, bool isIcon, Func<Task<string?>>? importFile = null)
    {
        _isIcon = isIcon;
        _importFile = importFile ?? (() => PickFileAsync(this, isIcon));
        SelectedBytes = currentBytes;
        Title = Localizer.Get(StringKeys.WinFormsImageDialogTitle);
        Width = 480;
        Height = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var localRadio = new RadioButton
        {
            Content = Localizer.Get(StringKeys.WinFormsImageDialogLocalResource),
            GroupName = "WinFormsImageResourceSource",
            IsChecked = true,
        };
        var projectRadio = new RadioButton
        {
            Content = Localizer.Get(StringKeys.WinFormsImageDialogProjectResource),
            GroupName = "WinFormsImageResourceSource",
            // Project resource browsing and import are not implemented yet; see the remarks above.
            IsEnabled = false,
        };

        var import = new Button { Content = Localizer.Get(StringKeys.WinFormsImageDialogImport), Name = "WinFormsImageDialogImport" };
        var clear = new Button { Content = Localizer.Get(StringKeys.WinFormsImageDialogClear), Name = "WinFormsImageDialogClear" };
        import.Click += async (_, _) => await ImportAsync();
        clear.Click += (_, _) =>
        {
            SelectedBytes = null;
            Cleared = true;
            _error.IsVisible = false;
            UpdatePreview();
        };

        var ok = new Button { Content = Localizer.Get(StringKeys.WinFormsEditorOk), IsDefault = true };
        var cancel = new Button { Content = Localizer.Get(StringKeys.WinFormsEditorCancel), IsCancel = true };
        ok.Click += (_, _) => Close(true);
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
        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children = { localRadio, localRow, projectRadio, _preview, _error, buttons },
        };
        Content = layout;
        UpdatePreview();
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
            UpdatePreview();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _error.Text = Localizer.Get(StringKeys.WinFormsImageDialogImportFailed, exception.Message);
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

    private void UpdatePreview()
    {
        _preview.Source = null;
        if (SelectedBytes is not { Length: > 0 } bytes) return;
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
