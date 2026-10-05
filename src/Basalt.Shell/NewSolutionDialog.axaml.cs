using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Basalt.Core.Model;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell;

/// <summary>Collects the data for the new solution and generates it on disk.</summary>
public partial class NewSolutionDialog : Window
{
    public NewSolutionDialog()
    {
        InitializeComponent();

        NameBox.Text = "MyApplication";
        PathBox.Text = DefaultParentDirectory();

        NameBox.TextChanged += (_, _) => UpdatePreview();
        PathBox.TextChanged += (_, _) => UpdatePreview();
        TemplateList.SelectionChanged += (_, _) => UpdateGuiPanelVisibility();
        UpdateGuiPanelVisibility();
        UpdatePreview();

        // Focused when the dialog opens, so it can be filled in without
        // reaching for the mouse first.
        Opened += (_, _) => NameBox.Focus();
    }

    /// <summary>
    /// Suggested folder: "Projects" in the user's home directory, independent of
    /// the platform.
    /// </summary>
    private static string DefaultParentDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Projects");

    private void UpdatePreview()
    {
        var name = NameBox.Text?.Trim();
        var parent = PathBox.Text?.Trim();

        PreviewLabel.Text = string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parent)
            ? ""
            : Localizer.Get(StringKeys.NewSolutionPreview, Path.Combine(parent, name));
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Get(StringKeys.NewSolutionChooseFolder),
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) PathBox.Text = path;
    }

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";

        var name = NameBox.Text?.Trim();
        var parent = PathBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionNameRequired);
            return;
        }

        // The name becomes a namespace and a type name, so it has to be a
        // valid Visual Basic identifier.
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionInvalidName);
            return;
        }

        if (string.IsNullOrWhiteSpace(parent))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionLocationRequired);
            return;
        }

        var template = SelectedTemplate;

        try
        {
            Directory.CreateDirectory(parent);
            var result = await SolutionTemplates.CreateAsync(parent, name, template);
            Close(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLabel.Text = ex.Message;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    // The GUI picker only matters for the desktop template, which can be built either on
    // Avalonia or on WinForms; every other template ignores it, so showing it there would
    // offer a choice the template generator never looks at.
    private void UpdateGuiPanelVisibility() => GuiPanel.IsVisible = SelectedBaseTemplate == ProjectTemplate.AvaloniaApp;

    // The template is carried on the item itself rather than derived from its
    // position, so reordering the list cannot silently change what the dialog
    // creates. A heading is not a template: the keyboard can land on one, and
    // then the desktop application is the fallback.
    private ProjectTemplate SelectedBaseTemplate => TemplateList.SelectedItem is ListBoxItem { Tag: string tag }
        && Enum.TryParse<ProjectTemplate>(tag, out var chosen) ? chosen : ProjectTemplate.AvaloniaApp;

    internal ProjectTemplate SelectedTemplate => SelectedBaseTemplate == ProjectTemplate.AvaloniaApp
        && GuiChoice.SelectedItem is ComboBoxItem { Tag: "WinForms" }
            ? ProjectTemplate.WinFormsApp : SelectedBaseTemplate;
}
