using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.ViewModels;
using Dock.Avalonia.Controls;

namespace Basalt.Tests;

/// <summary>
/// View > Reset Layout with documents open: they come back in the new layout, and the window still closes.
///
/// A tab taken out of its strip lost its close template and asked the window's template for the document's
/// view, which the document area already showed: closing the window then threw.
/// </summary>
public sealed class ResetLayoutTests
{
    private static void Invoke(MainWindow window, string header)
    {
        var entry = Leaves(window.MenuEntriesForTests()).First(e => e.Header == header);

        if (entry.Action is { } action) action();
        else entry.Command!.Execute(null);
    }

    private static IEnumerable<MenuEntry> Leaves(IReadOnlyList<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator) continue;

            if (entry.Children is { Count: > 0 } children)
            {
                foreach (var leaf in Leaves(children)) yield return leaf;
            }
            else
            {
                yield return entry;
            }
        }
    }

    /// <summary>The document tabs drawn now, by their titles.</summary>
    private static IReadOnlyList<string> TabTitles(TestWindow host) =>
        [.. host.Window.GetVisualDescendants().OfType<DocumentTabStripItem>()
            .Where(tab => tab.IsEffectivelyVisible)
            .SelectMany(tab => tab.GetVisualDescendants().OfType<TextBlock>())
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text!)];

    [AvaloniaFact]
    public async Task ACodeTabSurvivesAResetAndTheWindowStillCloses()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("basalt-reset-").FullName, "Module1.vb");
        await File.WriteAllTextAsync(file, "Module Module1\nEnd Module\n");

        var host = new TestWindow();
        await host.SettleAsync(3);
        await ((MainWindowViewModel)host.Window.DataContext!).OpenFileAsync(file);
        await host.SettleAsync(3);

        Invoke(host.Window, Localizer.Get(StringKeys.MenuViewResetLayout));
        await host.SettleAsync(5);

        Assert.Contains("Module1.vb", TabTitles(host));

        // Closing is where the tab, out of its strip, asked for the editor a second time.
        host.Dispose();
        await host.SettleAsync(5);
    }
}
