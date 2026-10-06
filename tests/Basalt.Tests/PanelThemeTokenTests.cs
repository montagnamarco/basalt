using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.SourceControl;

namespace Basalt.Tests;

/// <summary>
/// The panels that kept their own colours and sizes: the diff, the chat,
/// the solution tree and the panels that had no empty state.
///
/// A fixed header grey and a fixed grey card are both right on white and wrong
/// on a dark surface, so the tests ask the dark theme and check the colour
/// the panel drew is the theme's.
/// </summary>
public sealed class PanelThemeTokenTests
{
    private static ThemeVariant Variant(string name) =>
        name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static Color Token(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return (Color)value!;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 500, Height = 400 };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private const string Diff = "diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1,2 +1,2 @@\n keep\n-old\n+new\n";

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheDiffHeaderAndWashesAreTheThemesOwn(string name)
    {
        using var scope = new ThemeScope(Variant(name));
        var view = new DiffView();
        view.Show(Diff);
        var window = Show(view);

        try
        {
            var header = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text!.StartsWith("@@", StringComparison.Ordinal));
            var headerColour = Assert.IsAssignableFrom<ISolidColorBrush>(header.Foreground).Color;

            // Not the slate grey the header used to be in every theme.
            Assert.Equal(Token(window, "DescriptionForeground"), headerColour);

            var added = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Child is TextBlock { Text: "+new" });
            var wash = Assert.IsAssignableFrom<ISolidColorBrush>(added.Background);

            Assert.Equal(Token(window, "SuccessForeground").R, wash.Color.R);
            Assert.Equal(Token(window, "SuccessForeground").G, wash.Color.G);
            Assert.InRange(wash.Opacity, 0.10, 0.20);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheChatCardsAreWashesOfTheEditorTextColour(string name)
    {
        using var scope = new ThemeScope(Variant(name));
        var panel = new AiChatPanel();
        panel.Append(Microsoft.Extensions.AI.ChatRole.User, "How do I loop?");
        panel.Append(Microsoft.Extensions.AI.ChatRole.Assistant, "Use:\n```vb\nFor i = 1 To 3\n```");
        var window = Show(panel);

        try
        {
            var foreground = Token(window, "EditorForeground");
            var washes = panel.GetVisualDescendants().OfType<Border>()
                .Select(b => b.Background).OfType<ISolidColorBrush>()
                .Where(b => b.Color.R == foreground.R && b.Color.G == foreground.G && b.Color.B == foreground.B)
                .Select(b => b.Opacity).OrderBy(o => o).ToList();

            // The user's card (8%) and the code block (10%): each is the
            // theme's text colour at its own opacity, not a grey that ignores
            // the theme. (Other translucent borders, such as a control's
            // own, are not of the text colour and are not counted.)
            Assert.Equal(2, washes.Count);
            Assert.Equal(0.08, washes[0], 3);
            Assert.Equal(0.10, washes[1], 3);

            // And the colour really is this variant's: Light and Dark text
            // colours differ, so a fixed grey could not pass in both.
            Assert.Equal(name == "Dark", foreground.R > 0x80);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheChatButtonsUseThePanelFontNotElevenPixels()
    {
        var panel = new AiChatPanel();
        var window = Show(panel);

        try
        {
            var buttons = panel.GetVisualDescendants().OfType<Button>().ToList();

            Assert.NotEmpty(buttons);
            Assert.All(buttons, b => Assert.Equal(PanelMetrics.FontSize, b.FontSize));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSolutionExplorerRowsUseThePanelFontAndItsButtonsHaveTips()
    {
        var root = Directory.CreateTempSubdirectory("basalt-sefont-").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "Probe.vbproj"), "<Project />");
            File.WriteAllText(Path.Combine(root, "Program.vb"), "Module M\nEnd Module\n");

            var vm = new MainWindowViewModel();
            vm.Explorer.Load(Path.Combine(root, "Probe.vbproj"));
            var panel = new SolutionExplorerPanel(vm);
            var window = Show(panel);

            try
            {
                var names = panel.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text is "Program.vb" or "Probe.vbproj" or "Probe").ToList();

                Assert.NotEmpty(names);
                Assert.All(names, t => Assert.Equal(PanelMetrics.FontSize, t.FontSize));

                var tools = ((StackPanel)panel.Tools).Children.OfType<Button>().ToList();
                Assert.All(tools, b => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(b) as string)));
            }
            finally { window.Close(); }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [AvaloniaFact]
    public void ThePanelsThatHadNothingToSayNowSayIt()
    {
        var tests = new TestExplorerPanel();
        var history = new GitHistoryPanel();
        var tree = new ElementTreePanel();

        Assert.True(history.IsShowingEmptyState);
        Assert.True(tree.IsShowingEmptyState);
        Assert.Contains(Show(tests).GetVisualDescendants().OfType<EmptyState>(), e => e.IsVisible);
    }

    [AvaloniaFact]
    public async Task AReferencesSearchWithNoResultsShowsTheEmptyState()
    {
        var panel = new ReferencesPanel();
        await panel.ShowAsync("Missing", []);
        var window = Show(panel);

        try
        {
            Assert.Contains(panel.GetVisualDescendants().OfType<EmptyState>(), e => e.IsVisible);
            Assert.Contains("Missing", panel.Summary);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheGitHeaderButtonsExplainThemselves()
    {
        var panel = new GitChangesPanel();
        var window = Show(panel);

        try
        {
            var iconButtons = panel.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content is IconView).ToList();

            Assert.True(iconButtons.Count >= 2);
            Assert.All(iconButtons, b => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(b) as string)));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void EveryPanelStringHasItsResource()
    {
        // The keys added with these panels resolve to text, not to themselves:
        // Localizer returns the key when the resource is missing.
        foreach (var key in new[]
                 {
                     StringKeys.AiSend, StringKeys.AiYou, StringKeys.TestsRunAll, StringKeys.GitCommit,
                     StringKeys.BranchCreate, StringKeys.ReferencesEmpty, StringKeys.ToolboxNoMatches,
                     StringKeys.GitHistoryEmpty, StringKeys.ElementTreeEmpty, StringKeys.DatabaseNoTables,
                 })
            Assert.NotEqual(key, Localizer.Get(key));
    }
}
