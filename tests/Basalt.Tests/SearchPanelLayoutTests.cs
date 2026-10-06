using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>The search panel's text, layout, grouping and empty states.</summary>
public sealed class SearchPanelLayoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-searchlayout-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [AvaloniaFact]
    public void EveryLabelComesFromTheResources()
    {
        var panel = new SearchPanel();

        Assert.Equal(Localizer.Get(StringKeys.SearchFind), panel.FindButton.Content);
        Assert.Equal(Localizer.Get(StringKeys.SearchReplaceAll), panel.ReplaceAllButton.Content);
        Assert.Equal(Localizer.Get(StringKeys.SearchFind), panel.TermBox.PlaceholderText);
        Assert.Equal(Localizer.Get(StringKeys.SearchReplaceWith), panel.ReplacementBox.PlaceholderText);
        Assert.Equal(Localizer.Get(StringKeys.SearchFileTypes), panel.ExtensionsBox.PlaceholderText);
        Assert.Equal(Localizer.Get(StringKeys.SearchMatchCase), ToolTip.GetTip(panel.MatchCaseToggle));
        Assert.Equal(Localizer.Get(StringKeys.SearchWholeWord), ToolTip.GetTip(panel.WholeWordToggle));
        Assert.Equal(Localizer.Get(StringKeys.SearchUseRegex), ToolTip.GetTip(panel.RegexToggle));
    }

    /// <summary>
    /// "Find", "Replace all", "Searching…" and "No results." were string
    /// literals in the panel while every other panel read the resources, so
    /// they could never be translated. The check is on the source because
    /// only English ships: a translated run could not tell the two apart.
    /// </summary>
    [Fact]
    public void NoUserFacingLiteralIsLeftInThePanelSource()
    {
        var directory = Directory.GetCurrentDirectory();

        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "Basalt.Shell")))
            directory = Path.GetDirectoryName(directory);

        Assert.NotNull(directory);

        var source = File.ReadAllText(Path.Combine(directory!, "src", "Basalt.Shell", "Controls", "SearchPanel.cs"));

        // Text that is a word to the user: assigned as content, a placeholder, a summary.
        var literals = Regex.Matches(source, @"(?:Content|PlaceholderText|Text|Header) = (?:\$)?""([^""]*[A-Za-z]{3,}[^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.True(literals.Count == 0, $"Literals that should be in Strings.resx: {string.Join(", ", literals)}");
    }

    [AvaloniaFact]
    public async Task ResultsAreGroupedByFileWithTheirMatchCountsAndRelativeFolders()
    {
        Write("A.vb", "Dim target = 1\nDim target2 = 2\n");
        Write(Path.Combine("Views", "B.vb"), "Dim target = 3\n");

        var panel = new SearchPanel { SearchRoot = _root };
        await panel.SearchForAsync("target");

        var headers = panel.Results.Where(r => r.IsFileHeader).ToList();

        Assert.Equal(2, headers.Count);
        Assert.Equal(2, headers.Single(h => h.FilePath.EndsWith("A.vb")).MatchCount);
        Assert.Equal(1, headers.Single(h => h.FilePath.EndsWith("B.vb")).MatchCount);

        // The folder is shown relative to the searched one, and not at all at its root.
        Assert.Equal("Views", headers.Single(h => h.FilePath.EndsWith("B.vb")).Detail);
        Assert.Equal("", headers.Single(h => h.FilePath.EndsWith("A.vb")).Detail);
        Assert.Contains("3 results in 2 files", panel.Summary);
    }

    [AvaloniaFact]
    public async Task TheMatchedTextIsHighlightedInTheRenderedRow()
    {
        Write("A.vb", "    Dim target As Integer = 1\n");

        var panel = new SearchPanel { SearchRoot = _root };
        var window = new Window { Width = 600, Height = 300, Content = panel };
        window.Show();

        try
        {
            await panel.SearchForAsync("target");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var line = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Inlines is { Count: 3 });
            var runs = line.Inlines!.OfType<Run>().ToList();

            Assert.Equal("Dim ", runs[0].Text);
            Assert.Equal("target", runs[1].Text);
            Assert.NotNull(runs[1].Background);
            Assert.Null(runs[0].Background);
            Assert.Equal(" As Integer = 1", runs[2].Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TheMatchedLinesUseTheUsersEditorFontEvenAfterItChanges()
    {
        // Output follows the editor's font; the hit lines used to be drawn in
        // the default editor font whatever the user had chosen.
        Write("A.vb", "    Dim target As Integer = 1\n");

        var panel = new SearchPanel { SearchRoot = _root };
        panel.ApplySettings(new Basalt.Core.Settings.IdeSettings { Editor = { FontFamily = "Courier New" } });
        var window = new Window { Width = 600, Height = 300, Content = panel };
        window.Show();

        try
        {
            await panel.SearchForAsync("target");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            TextBlock Line() => window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Inlines is { Count: 3 });

            Assert.Equal("Courier New", Line().FontFamily.Name);

            panel.ApplySettings(new Basalt.Core.Settings.IdeSettings { Editor = { FontFamily = "Menlo" } });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal("Menlo", Line().FontFamily.Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePanelSaysWhatToDoThenSaysWhenNothingMatches()
    {
        Write("A.vb", "Public Class A\n");

        var panel = new SearchPanel { SearchRoot = _root };

        Assert.True(panel.IsShowingHint);

        await panel.SearchForAsync("Nothing like this");

        Assert.True(panel.IsShowingNoResults);
        Assert.False(panel.IsShowingHint);

        await panel.SearchForAsync("Class");

        Assert.False(panel.IsShowingNoResults);
        Assert.False(panel.IsShowingHint);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheButtonsAndInputsShareOneHeightFromThePanelMetrics(bool dark)
    {
        using var theme = new ThemeScope(dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        var panel = new SearchPanel { SearchRoot = _root };
        var window = new Window { Width = 600, Height = 300, Content = panel };
        window.Show();

        try
        {
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(PanelMetrics.FontSize, panel.TermBox.FontSize);
            Assert.Equal(PanelMetrics.FontSize, panel.FindButton.FontSize);
            Assert.Equal(panel.TermBox.Bounds.Height, panel.FindButton.Bounds.Height);
            Assert.Equal(panel.ReplacementBox.Bounds.Height, panel.ReplaceAllButton.Bounds.Height);
            Assert.Equal(panel.FindButton.Bounds.Width, panel.ReplaceAllButton.Bounds.Width);
        }
        finally { window.Close(); }
    }
}
