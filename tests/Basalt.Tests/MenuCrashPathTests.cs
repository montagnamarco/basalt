using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The path that took the application down: opening a file rebuilds the
/// Recent list, and on macOS the menu bar with it.
/// </summary>
public class MenuCrashPathTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-menu", Guid.NewGuid().ToString("N"));

    public MenuCrashPathTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task OpeningSeveralFilesLeavesTheMenuIntact()
    {
        // Each file opened is remembered, and a Recent list that changed
        // rebuilds the menu. On macOS that used to install a fresh NativeMenu
        // over the one the operating system was holding, and the second time
        // it happened inside a menu update the process died with
        // "The menu being updated does not match".
        //
        // Several files rather than one: the first open finds no menu to
        // reuse in some orders, and it is the second and third that matter.
        using var host = new TestWindow();

        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        var menu = NativeMenu.GetMenu(host.Window);

        for (var i = 0; i < 4; i++)
        {
            var file = Path.Combine(_root, $"File{i}.vb");

            await File.WriteAllTextAsync(
                file, $"Public Class File{i}\nEnd Class\n");

            await viewModel.OpenFileAsync(file);
        }

        if (IdeMenu.UsesSystemMenuBar)
        {
            // The same object throughout, and still full.
            Assert.Same(menu, NativeMenu.GetMenu(host.Window));
            Assert.NotEmpty(NativeMenu.GetMenu(host.Window)!.Items);
        }

        // And every file actually opened, so this is not passing because
        // nothing happened.
        Assert.Equal(4, viewModel.OpenDocuments.Count);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
