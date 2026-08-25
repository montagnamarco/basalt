using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The menu on a document tab.
///
/// Against a real window with real tabs, because the tab is templated by
/// Dock and the menu is attached when one appears: a test that built the menu
/// on its own would prove nothing about whether it ever reaches a tab.
/// </summary>
public sealed class DocumentTabMenuTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-tabmenu", Guid.NewGuid().ToString("N"));

    public DocumentTabMenuTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, MainWindowViewModel ViewModel)> OpenAsync(
        params string[] names)
    {
        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        foreach (var name in names.Length > 0 ? names : ["Program.vb"])
        {
            var file = Path.Combine(_root, name);

            await File.WriteAllTextAsync(file, "Module A\nEnd Module");
            await vm.OpenFileAsync(file);
        }

        await host.SettleAsync();

        return (host, vm);
    }

    private static IReadOnlyList<DocumentTabStripItem> TabsOf(TestWindow host) =>
        [.. host.Window.GetVisualDescendants().OfType<DocumentTabStripItem>()];

    [AvaloniaFact]
    public async Task GivesAnOpenDocumentTabAMenu()
    {
        var (host, _) = await OpenAsync();
        using var _2 = host;

        var tab = Assert.Single(TabsOf(host));

        Assert.NotNull(tab.ContextMenu);
    }

    [AvaloniaFact]
    public async Task OffersTheClosingCommandsAndTheRest()
    {
        var (host, _) = await OpenAsync();
        using var _2 = host;

        var headers = HeadersOf(TabsOf(host)[0]);

        Assert.Contains("Close", headers);
        Assert.Contains("Close Others", headers);
        Assert.Contains("Close All", headers);
        Assert.Contains("Close to the Right", headers);
        Assert.Contains("Copy Path", headers);
        Assert.Contains("Open Containing Folder", headers);
    }

    [AvaloniaFact]
    public async Task GivesEveryTabItsOwnMenu()
    {
        // Each menu closes over the document it belongs to, so two tabs must
        // not share one: closing from the second would close the first.
        var (host, _) = await OpenAsync("One.vb", "Two.vb");
        using var _2 = host;

        var tabs = TabsOf(host);

        Assert.Equal(2, tabs.Count);
        Assert.NotSame(tabs[0].ContextMenu, tabs[1].ContextMenu);
    }

    [AvaloniaFact]
    public async Task ClosesTheDocumentItWasAskedOn()
    {
        var (host, vm) = await OpenAsync("One.vb", "Two.vb");
        using var _2 = host;

        Assert.Equal(2, vm.OpenDocuments.Count);

        Click(TabsOf(host)[1], "Close");
        await host.SettleAsync();

        Assert.Single(TabsOf(host));
    }

    [AvaloniaFact]
    public async Task ClosesTheOthersAndKeepsTheOneAskedOn()
    {
        var (host, _) = await OpenAsync("One.vb", "Two.vb", "Three.vb");
        using var _2 = host;

        Assert.Equal(3, TabsOf(host).Count);

        Click(TabsOf(host)[0], "Close Others");
        await host.SettleAsync();

        var left = Assert.Single(TabsOf(host));

        Assert.Equal("One.vb", (left.DataContext as IdeDocument)?.Title);
    }

    [AvaloniaFact]
    public async Task ClosesOnlyWhatIsToTheRight()
    {
        var (host, _) = await OpenAsync("One.vb", "Two.vb", "Three.vb");
        using var _2 = host;

        Click(TabsOf(host)[0], "Close to the Right");
        await host.SettleAsync();

        Assert.Single(TabsOf(host));
    }

    [AvaloniaFact]
    public async Task ClosesEveryDocument()
    {
        var (host, _) = await OpenAsync("One.vb", "Two.vb");
        using var _2 = host;

        Click(TabsOf(host)[0], "Close All");
        await host.SettleAsync();

        Assert.Empty(TabsOf(host));
    }

    [AvaloniaFact]
    public async Task KeepsTheMenusOnTheTabsThatRemain()
    {
        // A tab that closes must not leave the others without a menu, which
        // is what happens if they are only attached once at startup.
        var (host, _) = await OpenAsync("One.vb", "Two.vb", "Three.vb");
        using var _2 = host;

        Click(TabsOf(host)[2], "Close");
        await host.SettleAsync();

        Assert.All(TabsOf(host), tab => Assert.NotNull(tab.ContextMenu));
    }

    /// <summary>The menu entries of a tab.</summary>
    private static IReadOnlyList<string> HeadersOf(DocumentTabStripItem tab) =>
    [
        .. (tab.ContextMenu?.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? "")
    ];

    /// <summary>Clicks one entry of a tab's menu.</summary>
    private static void Click(DocumentTabStripItem tab, string header) =>
        (tab.ContextMenu?.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>()
            .Single(item => item.Header?.ToString() == header)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
