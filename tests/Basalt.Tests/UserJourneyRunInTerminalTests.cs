using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// Run Without Debugging on a console program, the way a person does it: a real <see cref="MainWindow"/>, the Run
/// menu, a real build, a terminal tab that opens under the project's name, and keys typed into that tab.
/// </summary>
/// <remarks>
/// A console program runs in the terminal so that Console.ReadLine gets what is typed. A program with a window keeps
/// the redirected run, whose output goes to the Output panel. The terminal journey needs a Unix pseudo-terminal, so it
/// is skipped on Windows, as the other terminal journeys are. A program started there is ended when its tab closes,
/// and the window's Dispose closes it.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyRunInTerminalTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-run-terminal-").FullName;

    // The terminal reads its shell and its settings from the settings file, so each journey has one of its own.
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-run-terminal-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyRunInTerminalTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>
    /// The program the journey types to: it prompts, reads a line, and echoes it back in brackets. Its output
    /// depends on the input, so the test can tell that the typed text reached it.
    /// </summary>
    private const string ConsoleProgram = """
        Module Program
            Sub Main()
                Console.Write("name? ")
                Dim s = Console.ReadLine()
                Console.WriteLine("hello [" & s & "]")
            End Sub
        End Module
        """;

    private static string L(string key) => Localizer.Get(key);

    private string WriteProject(string outputType, string source)
    {
        var projectPath = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), source);
        return projectPath;
    }

    private static MenuEntry MenuEntryAt(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        return hit!;
    }

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        var entry = MenuEntryAt(window, path);

        if (entry.Action is { } action) action();
        else entry.Command!.Execute(null);
    }

    /// <summary>The terminal tabs in the bottom panel area, shell and program alike.</summary>
    private static TerminalTool[] TerminalTabs(MainWindow window) =>
        ((IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!).BottomArea!.VisibleDockables!.OfType<TerminalTool>().ToArray();

    /// <summary>The terminal view of a tab, found by the tab's title.</summary>
    private static TerminalView? TerminalNamed(MainWindow window, string title)
    {
        var tab = TerminalTabs(window).FirstOrDefault(t => t.Title == title);
        if (tab is null) return null;

        var contents = (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        return contents.TryGetValue(tab, out var view) ? view as TerminalView : null;
    }

    /// <summary>Lays out and pumps the interface thread until the condition holds or the time runs out.</summary>
    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int milliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>Types a line and presses Enter, the way a person at the keyboard does.</summary>
    private static void TypeLine(MainWindow window, string text)
    {
        window.KeyTextInput(text);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
    }

    /// <summary>True when a line of the terminal is exactly this text, ignoring the trailing blanks.</summary>
    private static bool HasLine(TerminalView terminal, string text) =>
        terminal.ScreenText.Split('\n').Any(line => line.TrimEnd() == text);

    // ------------------------------------------------------------------
    // Journey: Run Without Debugging on a console program opens a tab that
    // takes input, shows the program's output, and reports the exit.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task RunWithoutDebuggingOpensAConsoleProgramInATerminalTabThatTakesInput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's program runs on a Unix pseudo-terminal here.");

        var project = WriteProject("Exe", ConsoleProgram);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var window = host.Window;
        var viewModel = (MainWindowViewModel)window.DataContext!;

        InvokeMenu(window, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        // The build runs first, so the tab appears only after it has finished and the program has started.
        Assert.True(await WaitUntilAsync(host, () => TerminalNamed(window, "App") is not null, 180_000),
            "no terminal tab titled with the project opened for the program.");
        var terminal = TerminalNamed(window, "App")!;

        Assert.True(await WaitUntilAsync(host, () => terminal.ScreenText.Contains("name?", StringComparison.Ordinal)),
            "the program's prompt did not appear in its terminal.");
        Assert.True(viewModel.IsRunning);

        // What is typed reaches the program's Console.ReadLine, which echoes it back in its own line.
        host.Window.Activate();
        terminal.Focus();
        TypeLine(window, "Ada");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "hello [Ada]")),
            "the typed name did not reach the program.");
        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "[process exited with code 0]")),
            "the tab did not say how the program ended.");

        Assert.False(viewModel.IsRunning);
        Assert.Equal(Localizer.Get(StringKeys.StatusRunExited, 0), viewModel.StatusMessage);

        // The tab stays, and typing into it now reaches nothing.
        Assert.Contains(TerminalTabs(window), tab => tab.Title == "App");
        TypeLine(window, "after-exit");
        await host.SettleAsync(10);
        Assert.False(terminal.ScreenText.Contains("after-exit", StringComparison.Ordinal),
            "text typed after the program exited was shown in its tab.");
    }

    // ------------------------------------------------------------------
    // Journey: a program with a window is not moved into a terminal. It
    // runs as it always has, and its tab does not open.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ARunOfAWindowsProgramStaysOutOfTheTerminal()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The check is written for the Unix run, as the other terminal journeys are.");

        var marker = Path.Combine(_root, "windowed-ran.txt");
        var project = WriteProject("WinExe", """
            Module Program
                Sub Main()
                    System.IO.File.WriteAllText("windowed-ran.txt", "ran")
                End Sub
            End Module
            """);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var window = host.Window;
        var viewModel = (MainWindowViewModel)window.DataContext!;

        InvokeMenu(window, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        // The program ran, by the redirected path it always took, and no tab was opened for it.
        Assert.True(await WaitUntilAsync(host, () => File.Exists(marker), 180_000),
            "the windowed program did not run.");
        Assert.True(await WaitUntilAsync(host, () => !viewModel.IsRunning));
        Assert.Null(TerminalNamed(window, "App"));
        Assert.NotEmpty(viewModel.LastLaunchArgumentsForTests);
    }
}
