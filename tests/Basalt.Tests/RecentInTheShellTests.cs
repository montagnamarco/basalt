using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Recent solutions where they can be seen: in the File menu, and on the
/// screen that greets you.
/// </summary>
public sealed class RecentInTheShellTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-recentui-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static IEnumerable<MenuEntry> Walk(IEnumerable<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;

            if (entry.Children is { Count: > 0 } children)
                foreach (var child in Walk(children))
                    yield return child;
        }
    }

    [AvaloniaFact]
    public void TheFileMenuHasBothLists()
    {
        using var host = new TestWindow();

        var headers = Walk(host.Window.MenuEntriesForTests())
            .Select(e => e.Header)
            .ToList();

        Assert.Contains(Localizer.Get(StringKeys.MenuFileRecentSolutions), headers);
        Assert.Contains(Localizer.Get(StringKeys.MenuFileRecentFiles), headers);
    }

    [AvaloniaFact]
    public void AnEmptyListSaysSoInsteadOfBeingBlank()
    {
        using var host = new TestWindow();

        var recent = Walk(host.Window.MenuEntriesForTests())
            .First(e => e.Header == Localizer.Get(StringKeys.MenuFileRecentSolutions));

        var only = Assert.Single(recent.Children!);

        Assert.Equal(Localizer.Get(StringKeys.MenuFileRecentNone), only.Header);

        // Greyed rather than clickable: there is nothing behind it.
        Assert.False(only.IsEnabled);
    }

    [AvaloniaFact]
    public async Task OpeningAFilePutsItInTheList()
    {
        var file = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(file, "Module M\nEnd Module\n");

        using var host = new TestWindow();

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var recent = Walk(host.Window.MenuEntriesForTests())
            .First(e => e.Header == Localizer.Get(StringKeys.MenuFileRecentFiles));

        Assert.Contains(recent.Children!, e => e.Header == "Program.vb");
    }

    [AvaloniaFact]
    public async Task TheEntryCarriesItsFolderSoTwoOfAKindAreToldApart()
    {
        var first = Path.Combine(_root, "A");
        var second = Path.Combine(_root, "B");

        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        await File.WriteAllTextAsync(Path.Combine(first, "Index.vb"), "");
        await File.WriteAllTextAsync(Path.Combine(second, "Index.vb"), "");

        using var host = new TestWindow();

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(Path.Combine(first, "Index.vb"));
        await vm.OpenFileAsync(Path.Combine(second, "Index.vb"));
        await host.SettleAsync();

        var recent = Walk(host.Window.MenuEntriesForTests())
            .First(e => e.Header == Localizer.Get(StringKeys.MenuFileRecentFiles));

        var named = recent.Children!.Where(e => e.Header == "Index.vb").ToList();

        Assert.Equal(2, named.Count);

        // The label is the same for both, so the folder has to be somewhere.
        Assert.All(named, e => Assert.False(string.IsNullOrEmpty(e.ToolTip)));
        Assert.NotEqual(named[0].ToolTip, named[1].ToolTip);
    }

    [AvaloniaFact]
    public void TheWelcomeScreenHidesTheListWhenThereIsNothingToShow()
    {
        using var host = new TestWindow();

        var lists = host.Window.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => p.Name == "WelcomeRecent")
            .ToList();

        Assert.All(lists, l => Assert.False(l.IsVisible));
    }
}
