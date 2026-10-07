using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Renders real IDE windows to PNG files for a human to look at.
///
/// Every fact here is skipped unless <c>BASALT_SCREENSHOT_DIR</c> is set:
/// these are not assertions about behaviour, they are pictures, and nothing
/// in the ordinary test run should spend time producing files nobody asked
/// for. Point the variable at a folder and rerun this file alone
/// (<c>dotnet test tests/Basalt.Tests --filter "FullyQualifiedName~ScreenshotProbe"</c>)
/// to refresh the set.
/// </summary>
[Trait("Category", "Screenshots")]
[Collection(SettingsFileCollection.Name)]
public sealed class ScreenshotProbe : IDisposable
{
    private const int Width = 1440;
    private const int Height = 900;

    private readonly string _root = Directory.CreateTempSubdirectory("basalt-screenshots-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-screenshot-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public ScreenshotProbe() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch { /* best effort */ }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable("BASALT_SCREENSHOT_DIR");

    /// <summary>Every fact's first line: nothing below here runs in the ordinary suite.</summary>
    private static void SkipUnlessRequested() =>
        Assert.SkipWhen(string.IsNullOrEmpty(OutputDirectory),
            "Set BASALT_SCREENSHOT_DIR to render screenshots.");

    /// <summary>
    /// Renders the window at 1440x900, writes the PNG, and proves it is not
    /// blank: a window that failed to lay out paints one flat colour, and a
    /// file of plausible size can still be exactly that.
    /// </summary>
    private static void Save(Window window, string name)
    {
        window.Width = Width;
        window.Height = Height;
        window.UpdateLayout();

        using var target = new RenderTargetBitmap(new PixelSize(Width, Height));
        target.Render(window);

        var pixels = new byte[Width * Height * 4];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, Width, Height), pinned.AddrOfPinnedObject(),
                pixels.Length, Width * 4);
        }
        finally
        {
            pinned.Free();
        }

        AssertVaries(pixels, name);

        var path = Path.Combine(OutputDirectory!, name);
        using var file = File.Create(path);
        target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 2000, $"{name} is suspiciously small.");
    }

    /// <summary>
    /// Samples pixels across the frame and fails if they are all the same
    /// colour: a window that never laid out renders as one flat rectangle,
    /// which a file-size check alone would not catch.
    /// </summary>
    private static void AssertVaries(byte[] pixels, string name)
    {
        var (r0, g0, b0) = (pixels[0], pixels[1], pixels[2]);
        var varies = false;

        for (var i = 0; i < pixels.Length; i += 4 * 977)
        {
            if (pixels[i] != r0 || pixels[i + 1] != g0 || pixels[i + 2] != b0)
            {
                varies = true;
                break;
            }
        }

        Assert.True(varies, $"{name} looks blank: every sampled pixel is the same colour.");
    }

    private static string L(string key) => Localizer.Get(key);

    /// <summary>
    /// Clicks through the real menu action/command the same way
    /// <c>MainWindow</c>'s own menu handlers do, by walking the headers
    /// <see cref="MainWindow.MenuEntriesForTests"/> exposes.
    /// </summary>
    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + header);
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    // ------------------------------------------------------------------
    // 1. The welcome screen.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task MainEmpty()
    {
        SkipUnlessRequested();

        using var host = new TestWindow();
        await host.SettleAsync(8);

        Save(host.Window, "main-empty.png");
    }

    // ------------------------------------------------------------------
    // A WinForms form with a Button, opened the way a user opens one: a
    // Designer partial class plus a code-behind partial class, the same
    // split "Add Windows Form" produces (mirrors the fixture
    // WinFormsEventsPanelEndToEndTests uses for its own, unrelated test).
    // ------------------------------------------------------------------

    private const string DesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(40, 40)
                Me.Button1.Size = New System.Drawing.Size(90, 30)
                Me.Button1.Text = "Button1"
                Me.Controls.Add(Me.Button1)
                Me.ClientSize = New System.Drawing.Size(320, 240)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """;

    private const string CodeSource = """
        Partial Class Form1
            Private Sub Form1_Load(sender As Object, e As System.EventArgs)
                Dim title = Me.Text
            End Sub
        End Class
        """;

    private static string WriteWinFormsProject(string root)
    {
        var projectPath = Path.Combine(root, "Screenshots.vbproj");

        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <Import Include="System.Drawing" />
                <Import Include="System.Windows.Forms" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{Path.Combine(RepositoryRoot(), "src", "Basalt.Forms", "Basalt.Forms.csproj")}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "Form1.Designer.vb"), DesignerSource);
        File.WriteAllText(Path.Combine(root, "Form1.vb"), CodeSource);

        return projectPath;
    }

    /// <summary>Repository root, the same way other tests find it.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Basalt.slnx was not found above " + AppContext.BaseDirectory);
    }

    private async Task<(TestWindow Host, MainWindowViewModel Vm, WinFormsDesignSurface Surface)> OpenWinFormsDesignerAsync()
    {
        var project = WriteWinFormsProject(_root);
        var designerPath = Path.Combine(_root, "Form1.Designer.vb");

        var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await host.SettleAsync(10);

        var surface = Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (surface.RootForm is null && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Assert.NotNull(surface.RootForm);

        var canvas = (Canvas)typeof(WinFormsDesignSurface)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(surface)!;
        var point = canvas.TranslatePoint(new Point(60, 55), host.Window)!.Value;
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Button1", surface.SelectedControl?.Name);

        return (host, vm, surface);
    }

    // ------------------------------------------------------------------
    // 2. The designer, Button1 selected.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task WinFormsDesigner()
    {
        SkipUnlessRequested();

        var (host, _, _) = await OpenWinFormsDesignerAsync();
        using var dispose = host;
        await host.SettleAsync(5);

        // Saved first: Save sizes the window, and before that the docks have
        // no room and report themselves as not visible.
        Save(host.Window, "winforms-designer.png");

        Assert.True(host.Window.GetVisualDescendants().OfType<ToolboxPanel>().Any(p => p.IsEffectivelyVisible),
            "the Toolbox should be visible by default");
        Assert.True(host.Window.GetVisualDescendants().OfType<SolutionExplorerPanel>().Any(p => p.IsEffectivelyVisible),
            "Solution Explorer should be visible by default");
        Assert.True(host.Window.GetVisualDescendants().OfType<WinFormsPropertyPanel>().Any(p => p.IsEffectivelyVisible),
            "Properties should be visible by default");
    }

    // ------------------------------------------------------------------
    // 3. The code-behind, caret inside a method.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CodeEditorScreenshot()
    {
        SkipUnlessRequested();

        var (host, vm, _) = await OpenWinFormsDesignerAsync();
        using var dispose = host;

        var codePath = Path.Combine(_root, "Form1.vb");
        await vm.OpenFileAsync(codePath);
        await host.SettleAsync(8);

        var editor = host.Window.CodeEditorForTests(codePath);
        Assert.NotNull(editor);

        var text = editor!.GetVisualDescendants().OfType<TextEditor>().Single().Text;
        var caret = text.IndexOf("Dim title = Me.Text", StringComparison.Ordinal) + "Dim title = Me.Text".Length;
        editor.CaretOffsetForTests = caret;

        Save(host.Window, "code-editor.png");
    }

    // ------------------------------------------------------------------
    // 4. The same editor, completion list open after "Me.".
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CompletionScreenshot()
    {
        SkipUnlessRequested();

        var (host, vm, _) = await OpenWinFormsDesignerAsync();
        using var dispose = host;

        var codePath = Path.Combine(_root, "Form1.vb");
        await vm.OpenFileAsync(codePath);
        await host.SettleAsync(8);

        var editor = host.Window.CodeEditorForTests(codePath);
        Assert.NotNull(editor);

        var text = editor!.GetVisualDescendants().OfType<TextEditor>().Single().Text;
        var caret = text.IndexOf("Me.Text", StringComparison.Ordinal) + "Me.".Length;
        editor.CaretOffsetForTests = caret;

        await editor.ShowCompletionForTestsAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!editor.CompletionOpenForTests && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Assert.True(editor.CompletionOpenForTests, "the completion list did not open after \"Me.\".");

        Save(host.Window, "completion.png");
    }

    // ------------------------------------------------------------------
    // 5. Properties switched to Events for the selected Button.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PropertiesEventsScreenshot()
    {
        SkipUnlessRequested();

        var (host, _, _) = await OpenWinFormsDesignerAsync();
        using var dispose = host;

        var properties = Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
        var lightning = Assert.Single(properties.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "PropertyGridEvents");
        lightning.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await host.SettleAsync(5);

        Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsEventPanel>());

        Save(host.Window, "properties-events.png");
    }

    // ------------------------------------------------------------------
    // 6. The Settings window.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public void SettingsScreenshot()
    {
        SkipUnlessRequested();

        var window = new SettingsWindow(new SettingsStore(_settings), null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        try
        {
            Save(window, "settings.png");
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------------
    // 7. The Make Executable dialog.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public void MakeExecutableScreenshot()
    {
        SkipUnlessRequested();

        var window = new MakeExecutableDialog("Screenshots", [], Path.GetTempPath());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        try
        {
            Save(window, "make-exe.png");
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------------
    // 8. The Immediate window, shown the real way (View > Immediate).
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ImmediateScreenshot()
    {
        SkipUnlessRequested();

        var project = WriteWinFormsProject(_root);

        using var host = new TestWindow(project);
        await host.SettleAsync(8);

        InvokeMenu(host.Window, L(StringKeys.MenuView), L(StringKeys.ToolImmediate));
        await host.SettleAsync(8);

        var immediate = Assert.Single(host.Window.GetVisualDescendants().OfType<ImmediatePanel>());
        Assert.True(immediate.IsEffectivelyVisible);

        Save(host.Window, "immediate.png");
    }

    // ------------------------------------------------------------------
    // 9. Every top-level menu, opened.
    //
    // On macOS the in-window menu (WindowMenu) is hidden because the real
    // menu lives in the system menu bar (IdeMenu.UsesSystemMenuBar), which a
    // headless render cannot capture at all - it belongs to the operating
    // system, not to any Avalonia window. The same MenuEntry model is built
    // into an ordinary managed Menu here instead, which is the one other
    // platforms show in-window already.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task MenusScreenshots()
    {
        SkipUnlessRequested();

        using var host = new TestWindow();
        await host.SettleAsync(5);

        var entries = host.Window.MenuEntriesForTests();

        var menu = new Menu { ItemsSource = IdeMenu.BuildManaged(entries) };
        var window = new Window { Content = menu, Width = Width, Height = Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        try
        {
            // Realised before any submenu opens: Avalonia only builds a
            // MenuItem's own children once its submenu is first shown, so
            // this list is exactly the top-level entries, in order.
            var topLevel = menu.GetVisualDescendants().OfType<MenuItem>().ToList();
            Assert.Equal(entries.Count(e => !e.IsSeparator), topLevel.Count);

            foreach (var (entry, item) in entries.Where(e => !e.IsSeparator).Zip(topLevel))
            {
                item.Open();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var name = "menus-" + Sanitize(entry.Header) + ".png";
                Save(window, name);

                item.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static string Sanitize(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // ------------------------------------------------------------------
    // 10. The designer in the dark theme.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task MainDarkScreenshot()
    {
        SkipUnlessRequested();

        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = AppTheme.Dark;
        store.Save(settings);

        var (host, _, _) = await OpenWinFormsDesignerAsync();
        using var dispose = host;
        await host.SettleAsync(5);

        Save(host.Window, "main-dark.png");
    }
}
