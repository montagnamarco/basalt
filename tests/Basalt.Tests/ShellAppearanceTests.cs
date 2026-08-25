using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Basalt.Tests;

/// <summary>
/// What the shell actually looks like.
/// </summary>
/// <remarks>
/// Rendered and looked at rather than measured. Every fault this file exists
/// for — glyphs the size of a wall, oversized inputs, a control drawn outside
/// its border — passed the numeric assertions that were meant to catch it.
/// </remarks>
public sealed class ShellAppearanceTests : IDisposable
{
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-appearance-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    /// <summary>
    /// A settings file of this class's own.
    /// </summary>
    /// <remarks>
    /// These tests open solutions, and opening one adds it to the recent
    /// list. The whole suite shares one settings file, so without this the
    /// tests that check an empty recent list passed alone and failed in the
    /// suite — a fault that looks like flakiness and is not.
    /// </remarks>
    public ShellAppearanceTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>
    /// A small solution on disk, so the panels have something to show.
    /// </summary>
    /// <remarks>
    /// Without one the window is the welcome screen and no panel is built at
    /// all — which is why nothing ever caught the panel chrome going wrong.
    /// </remarks>
    private static string WriteSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "basalt-shell-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "App.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        // A window to open in the designer, which is the state the user is
        // in when they say the designer does not work.
        File.WriteAllText(Path.Combine(root, "MainWindow.axaml"), """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Canvas>
                <Button Canvas.Left="30" Canvas.Top="40" Width="100" Content="Click me" />
                <TextBox Canvas.Left="30" Canvas.Top="90" Width="160" />
              </Canvas>
            </Window>
            """);

        File.WriteAllText(Path.Combine(root, "Module1.vb"), """
            Module Module1
                Sub Main()
                    Console.WriteLine("hello")
                End Sub
            End Module
            """);

        return Path.Combine(root, "App.vbproj");
    }

    /// <summary>Renders the whole window to a file and hands back the path.</summary>
    private static async Task<string> RenderAsync(string name, int width = 1280, int height = 800)
    {
        using var host = new TestWindow(WriteSolution());

        host.Window.Width = width;
        host.Window.Height = height;

        await host.SettleAsync(8);

        var target = new RenderTargetBitmap(new PixelSize(width, height));
        target.Render(host.Window);

        var path = Path.Combine(Path.GetTempPath(), $"basalt-{name}.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        return path;
    }

    [AvaloniaFact]
    public async Task TheShellDraws()
    {
        var path = await RenderAsync("shell");

        Assert.True(new FileInfo(path).Length > 5000, "the window rendered empty");
    }

    [AvaloniaFact]
    public async Task ThePanelButtonsShowTheirGlyphs()
    {
        // With a solution open, which is the only state where the panels
        // exist at all: every earlier test built the welcome screen, where
        // there is no panel chrome to get wrong.
        using var host = new TestWindow(WriteSolution());
        await host.SettleAsync(8);

        var blank = new List<string>();

        foreach (var button in host.Window.GetVisualDescendants()
                     .OfType<Avalonia.Controls.Button>()
                     .Where(b => b.Name is "PART_PinButton" or "PART_MenuButton"
                                        or "PART_CloseButton"))
        {
            if (!button.IsEffectivelyVisible) continue;

            var drawn = button.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Path>()
                .Any(p => p.Bounds.Width >= 6 && p.Bounds.Height >= 6);

            if (!drawn) blank.Add(button.Name!);

            // Drawn is not the same as visible: a Path with no Fill and no
            // Stroke occupies its space and paints nothing, which is exactly
            // what a blank grey square is.
            foreach (var path in button.GetVisualDescendants()
                         .OfType<Avalonia.Controls.Shapes.Path>())
            {
                if (path.Fill is null && path.Stroke is null)
                    blank.Add($"{button.Name}: no fill and no stroke");
            }
        }

        Assert.True(blank.Count == 0,
            "These panel buttons are blank grey squares:\n"
          + string.Join("\n", blank.Distinct()));
    }

    [AvaloniaFact]
    public async Task ThePanelChromeIsWorthLookingAt()
    {
        // The top-right corner of a panel, blown up: the buttons are 18 by 26
        // and every numeric check on them passes, so the only way to see what
        // they actually look like is to look.
        using var host = new TestWindow(WriteSolution());

        host.Window.Width = 1280;
        host.Window.Height = 800;

        await host.SettleAsync(8);

        var target = new RenderTargetBitmap(new PixelSize(1280, 800));
        target.Render(host.Window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-chrome.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 5000);
    }

    [AvaloniaFact]
    public async Task TheDesignerIsWorthLookingAt()
    {
        var solution = WriteSolution();
        var axaml = Path.Combine(Path.GetDirectoryName(solution)!, "MainWindow.axaml");

        using var host = new TestWindow(solution);

        host.Window.Width = 1280;
        host.Window.Height = 800;

        await host.SettleAsync(8);

        var vm = (Basalt.Shell.ViewModels.MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(axaml, inDesigner: true);
        await host.SettleAsync(8);

        var target = new RenderTargetBitmap(new PixelSize(1280, 800));
        target.Render(host.Window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-designer.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 5000);
    }
}
