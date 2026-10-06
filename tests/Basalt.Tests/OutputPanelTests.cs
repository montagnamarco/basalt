using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>The Output panel: clear, word wrap, scroll lock, the editor's font and coloured diagnostics.</summary>
public sealed class OutputPanelTests
{
    private const string ErrorLine = "Views/MainForm.vb(12,5): error BC30451: 'Foo' is not declared. [App.vbproj]";
    private const string WarningLine = "Views/MainForm.vb(40,9): warning BC42024: Unused local variable: 'x'. [App.vbproj]";

    [Theory]
    [InlineData(ErrorLine, OutputLineKind.Error)]
    [InlineData(WarningLine, OutputLineKind.Warning)]
    [InlineData("[error] BC30451 MainForm.vb(12,5): 'Foo' is not declared.", OutputLineKind.Error)]
    [InlineData("[warning] BC42024 MainForm.vb(40,9): Unused.", OutputLineKind.Warning)]
    [InlineData("Build FAILED.", OutputLineKind.Error)]
    [InlineData("Build succeeded.", OutputLineKind.Success)]
    // The summary lines mention the words but report no problem.
    [InlineData("    0 Error(s)", OutputLineKind.Normal)]
    [InlineData("    0 Warning(s)", OutputLineKind.Normal)]
    [InlineData("  Determining projects to restore...", OutputLineKind.Normal)]
    // A tool started outside the IDE can still answer in the system's language.
    [InlineData("Compilazione NON riuscita.", OutputLineKind.Error)]
    [InlineData("Compilazione completata.", OutputLineKind.Success)]
    [InlineData("Program.vb(4,27): avviso BC42024: variabile non usata.", OutputLineKind.Warning)]
    public void LinesAreClassifiedByWhatTheyReport(string line, OutputLineKind expected) =>
        Assert.Equal(expected, OutputPanel.Classify(line));

    [AvaloniaFact]
    public void ClearEmptiesTheOutputAndShowsTheMessageAgain()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        viewModel.WriteOutput("one");
        viewModel.WriteOutput("two");
        Assert.False(panel.IsShowingEmptyState);

        var window = new Window { Width = 500, Height = 200, Content = panel };
        window.Show();

        try
        {
            var clear = window.GetVisualDescendants().OfType<Button>().First();
            clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Empty(viewModel.BuildOutput);
            Assert.True(panel.IsShowingEmptyState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WordWrapTogglesWrappingAndTheHorizontalScrollBar()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        viewModel.WriteOutput(new string('x', 400));

        var window = new Window { Width = 300, Height = 200, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            TextBlock Line() => window.GetVisualDescendants().OfType<SelectableTextBlock>().First();

            Assert.Equal(TextWrapping.NoWrap, Line().TextWrapping);
            Assert.Equal(ScrollBarVisibility.Auto, panel.Scroller.HorizontalScrollBarVisibility);
            var unwrapped = Line().Bounds.Height;

            panel.WrapToggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(TextWrapping.Wrap, Line().TextWrapping);
            Assert.Equal(ScrollBarVisibility.Disabled, panel.Scroller.HorizontalScrollBarVisibility);
            Assert.True(Line().Bounds.Height > unwrapped, "The long line did not wrap onto more rows.");

            panel.WrapToggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(TextWrapping.NoWrap, Line().TextWrapping);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ScrollToEndFollowsTheOutputUntilItIsSwitchedOff()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        var window = new Window { Width = 400, Height = 120, Content = panel };
        window.Show();

        try
        {
            Assert.True(panel.ScrollToEndToggle.IsChecked);

            for (var i = 0; i < 60; i++) viewModel.WriteOutput($"line {i}");
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            var atEnd = panel.Scroller.Offset.Y;
            Assert.True(atEnd > 0, "The output did not scroll to its end.");

            panel.Scroller.Offset = new Vector(0, 0);
            panel.ScrollToEndToggle.IsChecked = false;

            for (var i = 0; i < 20; i++) viewModel.WriteOutput($"more {i}");
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();

            Assert.Equal(0, panel.Scroller.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheFontComesFromTheEditorSettings()
    {
        using var viewModel = new MainWindowViewModel
        {
            LoadSettings = () => new IdeSettings { Editor = { FontFamily = "Courier New" } }
        };

        var panel = new OutputPanel(viewModel);
        viewModel.WriteOutput("hello");

        var window = new Window { Width = 400, Height = 120, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var line = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single();

            Assert.Equal("Courier New", panel.Font.Name);
            Assert.Equal("Courier New", line.FontFamily.Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ErrorAndWarningLinesAreDrawnInTheThemesTokens(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        viewModel.WriteOutput("  Restoring packages");
        viewModel.WriteOutput(WarningLine);
        viewModel.WriteOutput(ErrorLine);

        var window = new Window { Width = 700, Height = 160, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            window.TryFindResource("ErrorForeground", window.ActualThemeVariant, out var errorToken);
            window.TryFindResource("WarningForeground", window.ActualThemeVariant, out var warningToken);
            var error = (Color)errorToken!;
            var warning = (Color)warningToken!;
            var raster = Raster.Of(window);

            PixelRect Area(string text) => Raster.BoundsIn(
                window.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Text == text), window);

            // A fully covered pixel of a glyph is the token's colour.
            Assert.True(raster.Contains(Area(ErrorLine), error, 10), $"{name}: the error line is not {error}.");
            Assert.True(raster.Contains(Area(WarningLine), warning, 10), $"{name}: the warning line is not {warning}.");
            Assert.False(raster.Contains(Area("  Restoring packages"), error, 10), $"{name}: a plain line was drawn as an error.");
            Assert.False(raster.Contains(Area("  Restoring packages"), warning, 10), $"{name}: a plain line was drawn as a warning.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnlyTheLinesInViewAreControlsHoweverManyAreKept()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        viewModel.BuildOutput.AddRange(Enumerable.Range(0, 5000).Select(i => $"line {i}").ToList(), 5000);

        var window = new Window { Width = 500, Height = 200, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var realised = window.GetVisualDescendants().OfType<SelectableTextBlock>().Count();
            Assert.InRange(realised, 1, 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABatchOfLinesIsOneChangeAndTheCapDropsTheOldest()
    {
        using var viewModel = new MainWindowViewModel();
        var changes = 0;
        viewModel.BuildOutput.CollectionChanged += (_, _) => changes++;

        viewModel.BuildOutput.AddRange(Enumerable.Range(0, 4990).Select(i => $"a{i}").ToList(), 5000);
        viewModel.BuildOutput.AddRange(Enumerable.Range(0, 100).Select(i => $"b{i}").ToList(), 5000);

        Assert.Equal(2, changes);
        Assert.Equal(5000, viewModel.BuildOutput.Count);
        Assert.Equal("a90", viewModel.BuildOutput[0]);
        Assert.Equal("b99", viewModel.BuildOutput[^1]);
    }

    [AvaloniaFact]
    public void AChangeOfEditorFontReachesTheLinesAlreadyDrawn()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        viewModel.WriteOutput("hello");

        var window = new Window { Width = 400, Height = 120, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var line = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single();

            panel.ApplySettings(new IdeSettings { Editor = { FontFamily = "Courier New" } });

            Assert.Equal("Courier New", line.FontFamily.Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SettingsAreReadOnceNotOnEveryAttach()
    {
        var reads = 0;
        using var viewModel = new MainWindowViewModel
        {
            LoadSettings = () => { reads++; return new IdeSettings(); }
        };
        var panel = new OutputPanel(viewModel);
        var window = new Window { Width = 400, Height = 120, Content = panel };

        for (var i = 0; i < 3; i++)
        {
            window.Show();
            window.Content = null;
            window.Content = panel;
        }

        window.Close();
        Assert.Equal(1, reads);
    }

    [AvaloniaFact]
    public void ABurstOfLinesPostsOneScrollToTheEnd()
    {
        using var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);
        var window = new Window { Width = 400, Height = 120, Content = panel };
        window.Show();

        try
        {
            var scrolls = 0;
            panel.Scroller.ScrollChanged += (_, _) => scrolls++;
            for (var i = 0; i < 200; i++) viewModel.WriteOutput($"line {i}");
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();

            Assert.True(panel.Scroller.Offset.Y > 0);
            Assert.True(scrolls <= 4, $"{scrolls} scroll changes for one burst");
        }
        finally { window.Close(); }
    }
}
