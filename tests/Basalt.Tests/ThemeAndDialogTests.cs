using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Basalt.Shell;

namespace Basalt.Tests;

public class ThemeAndDialogTests
{
    [AvaloniaFact]
    public void LoadsTheNewSolutionDialogWithItsControls()
    {
        var dialog = new NewSolutionDialog();

        Assert.NotNull(dialog.FindControl<TextBox>("NameBox"));
        Assert.NotNull(dialog.FindControl<TextBox>("PathBox"));
        Assert.NotNull(dialog.FindControl<ListBox>("TemplateList"));

        // No language chooser any more: Visual Basic is the only one, and a
        // dialog that asks a question with one answer is a dialog that wastes
        // a click.
        Assert.Null(dialog.FindControl<RadioButton>("VbOption"));
    }

    [AvaloniaFact]
    public void ListsEveryAvailableTemplate()
    {
        var dialog = new NewSolutionDialog();
        var list = dialog.FindControl<ListBox>("TemplateList")!;

        var tags = list.Items.OfType<ListBoxItem>()
            .Select(i => i.Tag as string)
            .Where(t => t is not null)
            .ToList();

        // Every template the generator supports must be reachable from the UI.
        foreach (var template in Enum.GetNames<Basalt.Designer.ProjectTemplate>())
            Assert.Contains(template, tags);
    }

    [AvaloniaFact]
    public void CarriesItsTemplateOnEachItem()
    {
        // The template is read from the item's tag, so a reordered list cannot
        // silently change what gets created.
        var dialog = new NewSolutionDialog();
        var list = dialog.FindControl<ListBox>("TemplateList")!;

        foreach (var item in list.Items.OfType<ListBoxItem>())
        {
            var tag = Assert.IsType<string>(item.Tag);
            Assert.True(Enum.TryParse<Basalt.Designer.ProjectTemplate>(tag, out _),
                $"'{tag}' is not a known template");
        }
    }

    [AvaloniaFact]
    public void DefaultsToTheDesktopTemplate()
    {
        // By what is selected rather than by where it sits: the list is
        // grouped by language, so the desktop template is no longer the first
        // row — the first row is a heading. An index says nothing about which
        // template a reader would get.
        var dialog = new NewSolutionDialog();

        var selected = Assert.IsType<ListBoxItem>(
            dialog.FindControl<ListBox>("TemplateList")!.SelectedItem);

        Assert.Equal("AvaloniaApp", selected.Tag);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOfTheTargetPath()
    {
        var dialog = new NewSolutionDialog();

        // The preview reflects the name proposed when the dialog opens.
        var preview = dialog.FindControl<TextBlock>("PreviewLabel")!;
        var nome = dialog.FindControl<TextBox>("NameBox")!.Text!;

        Assert.Contains(nome, preview.Text);
        Assert.Contains("Verrà creata in", preview.Text);
    }

    [AvaloniaFact]
    public void ExposesTheVsCodeLightPaletteAsResources()
    {
        var app = Application.Current!;

        // Key colors of VS Code's Light+ theme.
        Assert.True(app.TryFindResource("StatusBarBackgroundBrush", out var statusBar));
        Assert.Equal(Color.Parse("#005FB8"), ((SolidColorBrush)statusBar!).Color);

        Assert.True(app.TryFindResource("SideBarBackgroundBrush", out var sideBar));
        Assert.Equal(Color.Parse("#F8F8F8"), ((SolidColorBrush)sideBar!).Color);

        Assert.True(app.TryFindResource("EditorBackgroundBrush", out var editor));
        Assert.Equal(Colors.White, ((SolidColorBrush)editor!).Color);
    }

    [AvaloniaFact]
    public void ShowsTheWelcomeScreenWhenNothingIsOpen()
    {
        // The main actions must stay reachable without the menu, which on
        // macOS lives outside the window.
        using var host = new TestWindow();
        var window = host.Window;

        var welcome = window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible);
    }

    [AvaloniaFact]
    public void UsesTheLightEditorBackgroundForTheMainWindow()
    {
        using var host = new TestWindow();
        var window = host.Window;

        var background = Assert.IsType<SolidColorBrush>(window.Background);
        Assert.Equal(Colors.White, background.Color);
    }

    [AvaloniaFact]
    public void OpensWithATemplateChosen()
    {
        // The list is grouped by language now, and a heading is not something
        // anyone can create: opening with one selected leaves the dialog
        // showing no choice at all, and Create falls back to whatever the
        // code's default happens to be.
        var dialog = new NewSolutionDialog();
        var list = dialog.FindControl<ListBox>("TemplateList")!;

        var selected = Assert.IsType<ListBoxItem>(list.SelectedItem);

        Assert.IsType<string>(selected.Tag);
    }

    [AvaloniaFact]
    public void GroupsTheTemplatesByLanguage()
    {
        // Basalt is an IDE for Visual Basic and its dialects, and the first
        // question anyone has is which dialect they are writing — not whether
        // the result is a window or a service.
        var dialog = new NewSolutionDialog();
        var list = dialog.FindControl<ListBox>("TemplateList")!;

        var headings = list.Items.OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains(headings, h => h?.Contains("VISUAL BASIC .NET") == true);
        Assert.Contains(headings, h => h?.Contains("DIALECTS") == true);

        // And the two newest are there to be chosen.
        var tags = list.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).ToList();

        Assert.Contains("Blazor", tags);
        Assert.Contains("VisualBasic6", tags);
    }
}
