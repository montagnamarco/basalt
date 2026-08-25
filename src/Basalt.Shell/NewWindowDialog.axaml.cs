using Avalonia.Controls;
using Avalonia.Interactivity;
using Basalt.Core.Model;
using Basalt.Designer;

namespace Basalt.Shell;

/// <summary>Asks for the name of the new window and creates it on disk.</summary>
public partial class NewWindowDialog : Window
{
    private readonly string _directory;
    private readonly SourceLanguage _language;

    public NewWindowDialog() : this(Directory.GetCurrentDirectory(), SourceLanguage.VisualBasic) { }

    public NewWindowDialog(string directory, SourceLanguage language)
    {
        InitializeComponent();
        _directory = directory;
        _language = language;

        LanguageLabel.Text = language == SourceLanguage.VisualBasic
            ? "Code-behind: Visual Basic (.vb)"
            : "Code-behind: C# (.cs)";

        NameBox.Text = "NuovaFinestra";

        // Focused when the dialog opens, so it can be filled in without
        // reaching for the mouse first.
        Opened += (_, _) => NameBox.Focus();
    }

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        var typeName = NameBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            ErrorLabel.Text = "Indicare un nome per la finestra.";
            return;
        }

        // The name becomes a VB or C# identifier: it must be validated before
        // writing files that would then fail to compile.
        if (!System.Text.RegularExpressions.Regex.IsMatch(typeName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            ErrorLabel.Text = "Il nome deve iniziare con una lettera e contenere solo lettere, cifre o trattini bassi.";
            return;
        }

        try
        {
            var namespaceName = NamespaceBox.Text?.Trim();
            var className = string.IsNullOrWhiteSpace(namespaceName)
                ? typeName
                : $"{namespaceName}.{typeName}";

            var result = await FormTemplates.CreateWindowAsync(
                _directory, className, _language, TitleBox.Text?.Trim());

            Close(result);
        }
        catch (IOException ex)
        {
            ErrorLabel.Text = ex.Message;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
