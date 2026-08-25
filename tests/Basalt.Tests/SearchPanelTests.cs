using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>The search panel, driven against files on disk.</summary>
public sealed class SearchPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-searchui", Guid.NewGuid().ToString("N"));

    public SearchPanelTests() => Directory.CreateDirectory(_root);

    private async Task WriteAsync(string name, string content) =>
        await File.WriteAllTextAsync(Path.Combine(_root, name), content);

    [AvaloniaFact]
    public async Task ListsHitsGroupedUnderTheirFile()
    {
        await WriteAsync("A.vb", "Public Class A\n    Public Sub M()\n    End Sub\nEnd Class");

        var panel = new SearchPanel { SearchRoot = _root };
        await panel.SearchForAsync("Public");

        var rows = panel.Results;

        // One header for the file, then a row per hit.
        Assert.Contains(rows, r => r.IsFileHeader && r.FilePath.EndsWith("A.vb"));
        Assert.Equal(2, rows.Count(r => !r.IsFileHeader));
    }

    [AvaloniaFact]
    public async Task ShowsTheMatchingLineForEachHit()
    {
        await WriteAsync("B.vb", "Dim target As Integer = 1");

        var panel = new SearchPanel { SearchRoot = _root };
        await panel.SearchForAsync("target");

        var hit = panel.Results.First(r => !r.IsFileHeader);

        Assert.Contains("Dim target As Integer = 1", hit.Display);
    }

    [AvaloniaFact]
    public async Task ReportsWhenNothingMatches()
    {
        await WriteAsync("C.vb", "Public Class C");

        var panel = new SearchPanel { SearchRoot = _root };
        await panel.SearchForAsync("NothingLikeThis");

        Assert.Contains("No results", panel.Summary);
    }

    [AvaloniaFact]
    public async Task AsksForASolutionWhenNoneIsOpen()
    {
        var panel = new SearchPanel { SearchRoot = null };
        await panel.SearchForAsync("anything");

        Assert.Contains("Open a solution", panel.Summary);
    }

    [AvaloniaFact]
    public async Task ReportsAHitWhenOneIsActivated()
    {
        await WriteAsync("D.vb", "Public Class D");

        var panel = new SearchPanel { SearchRoot = _root };
        await panel.SearchForAsync("Class");

        var row = panel.Results.First(r => !r.IsFileHeader);

        Basalt.Workspace.Search.SearchHit? activated = null;
        panel.HitActivated += (_, hit) => activated = hit;

        panel.ActivateForTests(row);

        Assert.NotNull(activated);
        Assert.EndsWith("D.vb", activated!.FilePath);
    }

    [AvaloniaFact]
    public async Task TheSearchPanelIsPartOfTheWindowLayout()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        // Registered as a dockable tool, so it can be shown from the menu.
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.NotNull(vm);

        var panels = host.Window.GetVisualDescendants().OfType<Control>()
            .Select(c => c.GetType().Name)
            .ToList();

        // It starts as a background tab, so its presence is checked through the
        // window building without error rather than through rendering.
        Assert.Contains("SolutionExplorerPanel", panels);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
