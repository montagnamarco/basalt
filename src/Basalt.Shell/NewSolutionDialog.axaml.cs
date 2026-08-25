using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Basalt.Core.Model;
using Basalt.Designer;

namespace Basalt.Shell;

/// <summary>Collects the data for the new solution and generates it on disk.</summary>
public partial class NewSolutionDialog : Window
{
    public NewSolutionDialog()
    {
        InitializeComponent();

        NameBox.Text = "MiaApplicazione";
        PathBox.Text = DefaultParentDirectory();

        NameBox.TextChanged += (_, _) => UpdatePreview();
        PathBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        // Focused when the dialog opens, so it can be filled in without
        // reaching for the mouse first.
        Opened += (_, _) => NameBox.Focus();
    }

    /// <summary>
    /// Suggested folder: "Progetti" in the user's home directory, independent of
    /// the platform.
    /// </summary>
    private static string DefaultParentDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Progetti");

    private void UpdatePreview()
    {
        var name = NameBox.Text?.Trim();
        var parent = PathBox.Text?.Trim();

        PreviewLabel.Text = string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parent)
            ? ""
            : $"Verrà creata in {Path.Combine(parent, name)}";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Scegli la cartella in cui creare la soluzione",
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
            ErrorLabel.Text = "Indicare un nome per la soluzione.";
            return;
        }

        // The name becomes a namespace and a type name, so it has to be a
        // valid Visual Basic identifier.
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            ErrorLabel.Text = "Il nome deve iniziare con una lettera e contenere solo lettere, cifre o trattini bassi.";
            return;
        }

        if (string.IsNullOrWhiteSpace(parent))
        {
            ErrorLabel.Text = "Indicare la cartella di destinazione.";
            return;
        }

        // The template is carried on the item itself rather than derived from
        // its position, so reordering the list cannot silently change what the
        // dialog creates.
        var template = TemplateList.SelectedItem is ListBoxItem { Tag: string tag }
                    && Enum.TryParse<ProjectTemplate>(tag, out var chosen)
            ? chosen
            : ProjectTemplate.AvaloniaApp;

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
}
