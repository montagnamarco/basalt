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
        Assert.NotNull(dialog.FindControl<TreeView>("TemplateTree"));

        // No language chooser any more: Visual Basic is the only one, and a
        // dialog that asks a question with one answer is a dialog that wastes
        // a click.
        Assert.Null(dialog.FindControl<RadioButton>("VbOption"));
    }

    [AvaloniaFact]
    public void ListsEveryAvailableTemplate()
    {
        // Every template the generator supports must be reachable as a leaf
        // of the tree: WinForms and Avalonia are now two separate leaves
        // under "Windows Desktop" rather than one item plus a GUI picker.
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        var leaves = Flatten(tree.ItemsSource).Select(LeafTemplate).Where(t => t is not null).ToList();
        foreach (var template in Enum.GetValues<Basalt.Designer.ProjectTemplate>())
            Assert.Contains(template, leaves);
    }

    [AvaloniaFact]
    public void SelectingBlazorAfterWinFormsStillYieldsBlazor()
    {
        // Each leaf carries its own template directly now, so an earlier
        // selection (WinForms) cannot leak into a later one (Blazor): there
        // is no shared GUI picker left for a stale choice to hide in.
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        SelectLeaf(tree, Basalt.Designer.ProjectTemplate.WinFormsApp);
        SelectLeaf(tree, Basalt.Designer.ProjectTemplate.Blazor);

        Assert.Equal(Basalt.Designer.ProjectTemplate.Blazor, dialog.SelectedTemplate);
    }

    [AvaloniaFact]
    public void CarriesItsTemplateOnEachLeaf()
    {
        // The template is read from the leaf's tag, so a reordered tree cannot
        // silently change what gets created.
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        foreach (var leaf in Flatten(tree.ItemsSource).Where(i => LeafTemplate(i) is not null))
        {
            var template = LeafTemplate(leaf)!.Value;
            Assert.True(Enum.IsDefined(template), $"'{template}' is not a known template");
        }
    }

    [AvaloniaFact]
    public void DefaultsToTheWindowsFormsTemplate()
    {
        // By what is selected rather than by where it sits in the tree: in
        // the spirit of Visual Basic 6, the dialog opens with the one
        // project every dialect's own classic tool would have made the easy
        // choice already picked.
        var dialog = new NewSolutionDialog();

        Assert.Equal(Basalt.Designer.ProjectTemplate.WinFormsApp, dialog.SelectedTemplate);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOfTheTargetPath()
    {
        var dialog = new NewSolutionDialog();

        // The preview reflects the name proposed when the dialog opens.
        var preview = dialog.FindControl<TextBlock>("PreviewLabel")!;
        var nome = dialog.FindControl<TextBox>("NameBox")!.Text!;

        Assert.Contains(nome, preview.Text);
        Assert.Contains("Will be created in", preview.Text);
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
        // A category heading is not something anyone can create: opening
        // with a leaf already selected leaves the dialog showing a real
        // choice, and Create enabled rather than falling back to whatever
        // the code's default happens to be.
        var dialog = new NewSolutionDialog();

        Assert.NotNull(dialog.SelectedTemplate);
        Assert.True(dialog.FindControl<Button>("CreateButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void GroupsTheTemplatesByDialect()
    {
        // Basalt is an IDE for Visual Basic and its dialects, and the first
        // question anyone has is which dialect they are writing — not whether
        // the result is a window or a service.
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        var headings = tree.ItemsSource!.OfType<TreeViewItem>()
            .Select(i => (i.Header as TextBlock)?.Text).ToList();

        Assert.Contains("Visual Basic .NET", headings);
        Assert.Contains("Visual Basic 6", headings);
        Assert.Contains("QuickBASIC", headings);

        // And the two newest are there to be chosen.
        var leaves = Flatten(tree.ItemsSource).Select(LeafTemplate).Where(t => t is not null).ToList();

        Assert.Contains(Basalt.Designer.ProjectTemplate.Blazor, leaves);
        Assert.Contains(Basalt.Designer.ProjectTemplate.VisualBasic6, leaves);
    }

    private static void SelectLeaf(TreeView tree, Basalt.Designer.ProjectTemplate template)
    {
        var item = Flatten(tree.ItemsSource).Single(i => LeafTemplate(i) == template);
        tree.SelectedItem = item;
    }

    private static Basalt.Designer.ProjectTemplate? LeafTemplate(TreeViewItem item) =>
        item.Tag?.GetType().GetProperty("Template")?.GetValue(item.Tag) as Basalt.Designer.ProjectTemplate?;

    private static IEnumerable<TreeViewItem> Flatten(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;

        foreach (var entry in items)
        {
            if (entry is not TreeViewItem item) continue;
            yield return item;
            foreach (var child in Flatten(item.ItemsSource)) yield return child;
        }
    }
}
