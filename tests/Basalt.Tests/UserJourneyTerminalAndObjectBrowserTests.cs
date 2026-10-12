using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.ObjectBrowser;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// The integrated terminal and the Object Browser, driven the way a person at
/// the IDE drives them: a real <see cref="MainWindow"/>, real menu commands,
/// real key presses routed to whatever has focus, and real projects on disk.
///
/// The terminal journeys need a Unix pseudo-terminal and are skipped on
/// Windows, as the other PTY tests are. Each shell started here is ended by
/// the window closing, and a journey that checks that reads the shell's own
/// process id, so a shell left behind would be caught by name.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyTerminalAndObjectBrowserTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-terminal-browser-").FullName;

    // The terminal reads its shell from the settings file, so every journey
    // gets one of its own: the developer's shell setting must not change what
    // is being tested.
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-terminal-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyTerminalAndObjectBrowserTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private static string L(string key) => Localizer.Get(key);

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
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

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 200)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    private static IdeDockFactory FactoryOf(MainWindow window) =>
        (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static Dictionary<IDockable, Control> ContentsOf(MainWindow window) =>
        (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static string WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteConsoleProject(string root, string programSource) =>
        WriteProject(root, "Exe", "App", programSource);

    private static string WriteProject(string root, string outputType, string name, string source)
    {
        var projectPath = Path.Combine(root, $"{name}.vbproj");
        WriteFile(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(Path.Combine(root, outputType == "Exe" ? "Program.vb" : "Shop.vb"), source);
        return projectPath;
    }

    // ==================================================================
    // Terminal
    // ==================================================================

    /// <summary>The terminal the window shows as active in the bottom area: the one a person just opened.</summary>
    private static TerminalView ActiveTerminalOf(MainWindow window)
    {
        var tool = Assert.IsType<TerminalTool>(FactoryOf(window).BottomArea!.ActiveDockable);
        return Assert.IsType<TerminalView>(ContentsOf(window)[tool]);
    }

    private static int TerminalCount(MainWindow window) =>
        FactoryOf(window).BottomArea!.VisibleDockables!.OfType<TerminalTool>().Count();

    /// <summary>
    /// Opens a terminal from View > New Terminal and gives it the keyboard the
    /// way a click in it does. The headless window is not frontmost by itself,
    /// so it is activated first; without that the terminal never has focus and
    /// typed text goes nowhere.
    /// </summary>
    private static async Task<TerminalView> OpenTerminalFromMenuAsync(TestWindow host)
    {
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.MenuViewNewTerminal));
        await host.SettleAsync(10);

        return FocusActiveTerminal(host);
    }

    private static TerminalView FocusActiveTerminal(TestWindow host)
    {
        var terminal = ActiveTerminalOf(host.Window);
        host.Window.Activate();
        terminal.Focus();
        return terminal;
    }

    /// <summary>The keyboard modifiers a gesture needs, as the window receives them.</summary>
    private static RawInputModifiers RawModifiersOf(KeyModifiers modifiers)
    {
        var raw = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) raw |= RawInputModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Meta)) raw |= RawInputModifiers.Meta;
        if (modifiers.HasFlag(KeyModifiers.Shift)) raw |= RawInputModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Alt)) raw |= RawInputModifiers.Alt;
        return raw;
    }

    /// <summary>Types a line and presses Enter, the way a person at the keyboard does.</summary>
    private static void TypeLine(MainWindow window, string text)
    {
        window.KeyTextInput(text);
        PressKey(window, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
    }

    /// <summary>True when a line of the terminal is exactly this text, ignoring the trailing blanks.</summary>
    private static bool HasLine(TerminalView terminal, string text) =>
        terminal.ScreenText.Split('\n').Any(line => line.TrimEnd() == text);

    /// <summary>The text the terminal's own text box shows, which lags the model by one dispatch.</summary>
    private static string DisplayedTextOf(TerminalView terminal) =>
        terminal.GetVisualDescendants().OfType<TextBox>().First().Text ?? "";

    /// <summary>
    /// Clicks an entry of the terminal's context menu. A click raises the
    /// entry's Click, which is what a person choosing it does.
    /// </summary>
    private static void ClickTerminalMenu(TerminalView terminal, string header)
    {
        var item = terminal.Menu?.Items.OfType<MenuItem>().Single(i => (string?)i.Header == header)
            ?? throw new InvalidOperationException("the terminal has no menu.");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    /// <summary>The id the shell reports for itself; read from output, not from the typed command.</summary>
    private static async Task<int> ShellProcessIdAsync(TestWindow host, TerminalView terminal)
    {
        TypeLine(host.Window, "echo basalt-pid-$$");

        var pattern = new Regex(@"^basalt-pid-(\d+)$");
        int? pid = null;

        Assert.True(await WaitUntilAsync(host, () =>
        {
            foreach (var line in terminal.ScreenText.Split('\n'))
            {
                var match = pattern.Match(line.TrimEnd());
                if (match.Success) pid = int.Parse(match.Groups[1].Value);
            }
            return pid is not null;
        }), "the shell did not print its process id.");

        return pid!.Value;
    }

    /// <summary>True once no process has that id, or the process has exited.</summary>
    private static bool ProcessIsGone(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<bool> WaitUntilGoneAsync(int pid, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!ProcessIsGone(pid) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        return ProcessIsGone(pid);
    }

    // ------------------------------------------------------------------
    // Terminal journey 1: View > New Terminal opens a shell in the solution
    // folder, and a command typed into it prints its output.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task OpeningATerminalFromTheMenuStartsAShellInTheSolutionFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        var project = WriteConsoleProject(_root, "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(terminal.WorkingDirectory));

        TypeLine(w, "echo basalt-journey");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-journey")),
            "the terminal did not print the output of echo.");

        // The shell really runs in the folder: its own pwd names it.
        TypeLine(w, "pwd");

        var folder = Path.GetFileName(_root);
        Assert.True(await WaitUntilAsync(host,
                () => terminal.ScreenText.Split('\n').Any(line => line.TrimEnd().EndsWith(folder, StringComparison.Ordinal)
                                                              && !line.Contains("pwd", StringComparison.Ordinal))),
            "pwd did not print the solution folder.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 2: the terminal's shortcut, Ctrl+`, opens one too.
    // The shortcut is read from the menu entry rather than typed in, so a
    // changed binding is followed rather than failing here.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheTerminalShortcutOpensAShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var gesture = MenuEntryAt(w, L(StringKeys.MenuView), L(StringKeys.MenuViewNewTerminal)).Gesture
            ?? throw new InvalidOperationException("the New Terminal menu entry has no shortcut.");

        var before = TerminalCount(w);

        PressKey(w, gesture.Key, RawModifiersOf(gesture.KeyModifiers), PhysicalKey.Backquote);
        await host.SettleAsync(10);

        Assert.Equal(before + 1, TerminalCount(w));

        var terminal = FocusActiveTerminal(host);
        TypeLine(w, "echo basalt-shortcut");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-shortcut")),
            "the terminal opened by the shortcut did not run a command.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 3: a command that prints many lines leaves the view
    // scrolled to the end, so the prompt after the last line is in view.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task APrintingCommandScrollsToItsLastLine()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        var scroller = terminal.GetVisualDescendants().OfType<ScrollViewer>().First();

        TypeLine(w, "seq 1 300");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "300")),
            "the command did not print its last line.");

        await host.SettleAsync(5);

        Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
            "the output is not taller than the view, so there was nothing to scroll.");

        var bottom = scroller.Extent.Height - scroller.Viewport.Height;
        Assert.True(scroller.Offset.Y >= bottom - 1,
            $"the view is at {scroller.Offset.Y:0}, not at the end ({bottom:0}): the last line is out of sight.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 4: Clear, from the terminal's menu, empties what is
    // shown, and the shell keeps working afterwards.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ClearEmptiesTheTerminalAndTheShellKeepsWorking()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);

        TypeLine(w, "echo basalt-before-clear");
        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-before-clear")),
            "the terminal did not print the output before Clear.");

        ClickTerminalMenu(terminal, L(StringKeys.TerminalClear));

        Assert.True(await WaitUntilAsync(host, () => !DisplayedTextOf(terminal).Contains("basalt-before-clear", StringComparison.Ordinal)),
            "Clear did not empty the terminal's display.");
        Assert.DoesNotContain("basalt-before-clear", terminal.ScreenText);

        TypeLine(w, "echo basalt-after-clear");
        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-after-clear")),
            "the shell did not run a command after Clear.");
        Assert.True(terminal.IsRunning);
    }

    // ------------------------------------------------------------------
    // Terminal journey 5: closing a terminal ends its shell, and reopening
    // gives a fresh, working one. Closing is done the way the panel's title
    // bar does it: Dock closes the tool.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ClosingATerminalEndsItsShellAndReopeningGivesAFreshOne()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;
        var factory = FactoryOf(w);

        var terminal = await OpenTerminalFromMenuAsync(host);
        var tool = (TerminalTool)factory.BottomArea!.ActiveDockable!;
        var pid = await ShellProcessIdAsync(host, terminal);

        factory.CloseDockable(tool);
        await host.SettleAsync(5);

        Assert.DoesNotContain(tool, factory.BottomArea!.VisibleDockables!);
        Assert.True(await WaitUntilGoneAsync(pid),
            $"closing the terminal left its shell (process {pid}) running.");

        var reopened = await OpenTerminalFromMenuAsync(host);
        Assert.NotSame(terminal, reopened);

        TypeLine(w, "echo basalt-reopened");
        Assert.True(await WaitUntilAsync(host, () => HasLine(reopened, "basalt-reopened")),
            "the reopened terminal did not run a command.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 6: the terminal's own "Close Terminal" entry closes it
    // and ends its shell, the same as closing it from the title bar.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheTerminalsCloseEntryEndsItsShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;
        var factory = FactoryOf(w);

        var terminal = await OpenTerminalFromMenuAsync(host);
        var tool = (TerminalTool)factory.BottomArea!.ActiveDockable!;
        var pid = await ShellProcessIdAsync(host, terminal);

        ClickTerminalMenu(terminal, L(StringKeys.TerminalCloseTerminal));
        await host.SettleAsync(5);

        Assert.DoesNotContain(tool, factory.BottomArea!.VisibleDockables!);
        Assert.True(await WaitUntilGoneAsync(pid),
            $"Close Terminal left its shell (process {pid}) running.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 7: closing the window ends every shell it started.
    // The shell's id is read from its output, so the check is about that
    // very process, not about a count of processes.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ClosingTheWindowEndsTheShellProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        var project = WriteConsoleProject(_root, "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        var pid = await ShellProcessIdAsync(host, terminal);
        Assert.False(ProcessIsGone(pid), "the shell was not running before the window closed.");

        host.Dispose();

        Assert.True(await WaitUntilGoneAsync(pid),
            $"closing the window left its shell (process {pid}) running.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 8: the Scrollback lines setting limits what the
    // terminal keeps. A user who sets 100 expects the first of 300 printed
    // lines to be gone.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheScrollbackSettingLimitsWhatTheTerminalKeeps()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        WriteFile(_settings, """{ "Terminal": { "ScrollbackLines": 100 } }""");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        TypeLine(w, "seq 1 300");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "300")),
            "the command did not print its last line.");

        Assert.False(HasLine(terminal, "1"),
            "the first line of 300 is still kept although Scrollback lines is 100.");
        Assert.True(terminal.ScreenText.Split('\n').Length <= 105,
            $"the terminal keeps {terminal.ScreenText.Split('\n').Length} lines, more than the 100 set.");
    }

    /// <summary>The window's clipboard, which the terminal's menu also uses.</summary>
    private static IClipboard ClipboardOf(TestWindow host) =>
        TopLevel.GetTopLevel(host.Window)?.Clipboard
        ?? throw new InvalidOperationException("the window has no clipboard.");

    /// <summary>
    /// Waits until the clipboard's text satisfies <paramref name="until"/>. The
    /// menu's Copy and Paste finish asynchronously, so the answer is polled.
    /// </summary>
    private static async Task<string?> ClipboardTextAsync(TestWindow host, Func<string?, bool> until)
    {
        string? text = null;
        for (var i = 0; i < 200; i++)
        {
            text = await ClipboardOf(host).TryGetTextAsync();
            if (until(text)) break;
            await host.SettleAsync(1);
        }
        return text;
    }

    // ------------------------------------------------------------------
    // Terminal journey 9: Copy, from the terminal's menu, puts the selected
    // text on the clipboard, and only that text.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CopyPutsTheSelectedTextOnTheClipboard()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        TypeLine(w, "echo basalt-copy-me");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-copy-me")),
            "the terminal did not print the line to copy.");

        // The printed line is the last occurrence: the typed command also contains the text.
        var box = terminal.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(await WaitUntilAsync(host, () => DisplayedTextOf(terminal).Split('\n').Any(line => line.TrimEnd() == "basalt-copy-me")),
            "the terminal's display did not show the printed line.");

        var start = box.Text!.LastIndexOf("basalt-copy-me", StringComparison.Ordinal);
        box.SelectionStart = start;
        box.SelectionEnd = start + "basalt-copy-me".Length;

        ClickTerminalMenu(terminal, L(StringKeys.TerminalCopy));

        var copied = await ClipboardTextAsync(host, text => text == "basalt-copy-me");
        Assert.Equal("basalt-copy-me", copied);
    }

    // ------------------------------------------------------------------
    // Terminal journey 10: Paste, from the terminal's menu, types the
    // clipboard's text into the shell, which runs it when Enter is pressed.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task PasteTypesTheClipboardTextIntoTheShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("echo basalt-pasted"));
        await ClipboardOf(host).SetDataAsync(transfer);

        var terminal = await OpenTerminalFromMenuAsync(host);
        ClickTerminalMenu(terminal, L(StringKeys.TerminalPaste));

        // Enter is pressed only once the shell has echoed the pasted text, since
        // the paste completes asynchronously.
        Assert.True(await WaitUntilAsync(host, () => terminal.ScreenText.Contains("echo basalt-pasted", StringComparison.Ordinal)),
            "Paste did not type the clipboard's text into the shell.");

        PressKey(w, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "basalt-pasted")),
            "the shell did not run the pasted command.");
    }

    // ------------------------------------------------------------------
    // Terminal journey 11: Close Terminal ends the shell before the window is
    // asked to remove the terminal. Checked on the terminal alone, so it does
    // not depend on the window's answer.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CloseEndsTheShellBeforeTheWindowIsAsked()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);

        var terminal = await OpenTerminalFromMenuAsync(host);
        var pid = await ShellProcessIdAsync(host, terminal);

        var asked = false;
        var shellRunningWhenAsked = true;
        terminal.CloseRequested += (_, _) =>
        {
            asked = true;
            shellRunningWhenAsked = !ProcessIsGone(pid);
        };

        ClickTerminalMenu(terminal, L(StringKeys.TerminalCloseTerminal));

        Assert.True(asked, "Close Terminal did not ask the window to remove the terminal.");
        Assert.False(shellRunningWhenAsked, "the window was asked to remove the terminal while its shell was still running.");
        Assert.True(await WaitUntilGoneAsync(pid), $"Close Terminal left its shell (process {pid}) running.");
        Assert.False(terminal.IsRunning);
    }

    // ------------------------------------------------------------------
    // Terminal journey 12: changing the Scrollback setting while a terminal is
    // open applies to it. Shrinking drops the oldest lines at once; growing
    // keeps every line printed afterwards.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ChangingTheScrollbackSettingAppliesToAnOpenTerminal()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The terminal's shell runs on a Unix pseudo-terminal here.");

        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var terminal = await OpenTerminalFromMenuAsync(host);
        TypeLine(w, "seq 1 300");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "300")),
            "the command did not print its last line.");
        Assert.True(HasLine(terminal, "1"), "with the default limit the first line is kept.");

        var settings = new SettingsStore(_settings).Load();

        // Shrinking: the oldest lines go, the newest stay.
        settings.Terminal.ScrollbackLines = 100;
        w.ApplySettingsForTests(settings);

        Assert.False(HasLine(terminal, "1"), "the first line is still kept after Scrollback lines was set to 100.");
        Assert.True(HasLine(terminal, "300"), "shrinking the scrollback dropped the newest line.");
        Assert.True(terminal.ScreenText.Split('\n').Length <= 105,
            $"the terminal keeps {terminal.ScreenText.Split('\n').Length} lines, more than the 100 set.");

        // Growing: nothing is dropped from here on, so all 200 lines printed now stay.
        settings.Terminal.ScrollbackLines = 1000;
        w.ApplySettingsForTests(settings);

        TypeLine(w, "seq 301 500");

        Assert.True(await WaitUntilAsync(host, () => HasLine(terminal, "500")),
            "the command did not print its last line.");

        var kept = Enumerable.Range(301, 200).Count(n => HasLine(terminal, n.ToString()));
        Assert.Equal(200, kept);
    }

    // ==================================================================
    // Object Browser
    // ==================================================================

    private const string ShopSource = """
        Namespace Shop
            ''' <summary>A person who buys things.</summary>
            Public Class Customer
                ''' <summary>Shows the text in a box.</summary>
                Public Sub Show(text As String)
                End Sub

                ''' <summary>Adds up the amounts of the purchases.</summary>
                Public Function Total(amount As Integer) As Integer
                    Return amount
                End Function
            End Class

            ''' <summary>Something that has a price.</summary>
            Public Interface IPriced
                ''' <summary>The price in the shop's currency.</summary>
                Function Price() As Decimal
            End Interface

            ''' <summary>The states of an order.</summary>
            Public Enum Status
                Open
                Closed
            End Enum
        End Namespace
        """;

    /// <summary>The declaration line of a member in the file, one-based, as the editor counts lines.</summary>
    private static int LineOf(string path, string declaration)
    {
        var lines = File.ReadAllLines(path);
        var index = Array.FindIndex(lines, line => line.Contains(declaration, StringComparison.Ordinal));
        Assert.True(index >= 0, $"'{declaration}' is not in {path}.");
        return index + 1;
    }

    /// <summary>Selects a row of the type list the way a click does.</summary>
    private static void SelectTypeRow(ObjectBrowserPanel browser, object row) =>
        browser.TypeList.SelectedItem = row;

    /// <summary>Waits until the type list has a type of this name, and returns its row.</summary>
    private static async Task<BrowserTypeRow> TypeRowAsync(TestWindow host, ObjectBrowserPanel browser, string name)
    {
        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserTypeRow>().Any(row => row.Symbol.Name == name)),
            $"the Object Browser does not list {name}.");

        return browser.TypeRows.OfType<BrowserTypeRow>().First(row => row.Symbol.Name == name);
    }

    /// <summary>Expands a namespace in the type list by double-clicking it, which opens it and reads its types.</summary>
    private static async Task ExpandNamespaceAsync(TestWindow host, ObjectBrowserPanel browser, string name)
    {
        var row = browser.TypeRows.OfType<BrowserNamespaceRow>().Single(ns => ns.Name == name);
        SelectTypeRow(browser, row);
        browser.TypeList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserNamespaceRow>().Any(ns => ns.Name == name && ns.IsExpanded)),
            $"double-clicking the namespace {name} did not expand it.");
    }

    private static async Task<ObjectBrowserPanel> OpenObjectBrowserAsync(TestWindow host)
    {
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolObjectBrowser));
        await host.SettleAsync(10);

        var browser = w.GetVisualDescendants().OfType<ObjectBrowserPanel>().Single();
        Assert.True(await WaitUntilAsync(host, () => browser.Libraries.Count > 0, maxPasses: 400),
            "the Object Browser never read the libraries of the solution.");

        return browser;
    }

    // ------------------------------------------------------------------
    // Object Browser journey 1: a VB class library. The namespace expands to
    // its class, interface and enum; the class lists its members with their
    // signatures; a search narrows them; the description pane names the
    // signature and the XML summary; double-clicking a member opens its
    // declaration.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task BrowsingAVbLibraryListsItsTypesMembersAndDescriptionsAndOpensADeclaration()
    {
        var project = WriteProject(_root, "Library", "Shop", ShopSource);
        var shopPath = Path.Combine(_root, "Shop.vb");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var browser = await OpenObjectBrowserAsync(host);

        // Select the class library in the selector, as a person does.
        var library = browser.Libraries.Single(l => l.Kind == BrowserLibraryKind.Project);
        browser.LibraryBox.SelectedIndex = browser.Libraries.ToList().IndexOf(library);

        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserNamespaceRow>().Any(ns => ns.Name == "Shop")),
            "the library's namespace Shop is not listed.");

        await ExpandNamespaceAsync(host, browser, "Shop");

        // The namespace holds a class, an interface and an enum.
        var customerRow = await TypeRowAsync(host, browser, "Customer");
        await TypeRowAsync(host, browser, "IPriced");
        await TypeRowAsync(host, browser, "Status");

        // Selecting the class lists its members with their signatures.
        SelectTypeRow(browser, customerRow);

        Assert.True(await WaitUntilAsync(host,
                () => browser.MemberRows.Any(row => row.Symbol.Name == "Total")),
            "selecting Customer did not list its members.");

        Assert.Contains(browser.MemberRows, row => row.Symbol.Signature == "Public Sub Show(text As String)");
        Assert.Contains(browser.MemberRows, row => row.Symbol.Signature == "Public Function Total(amount As Integer) As Integer");

        // The description pane names the class and carries its XML summary.
        Assert.Equal("Public Class Customer", browser.SignatureText);
        Assert.Contains("Namespace Shop", browser.DetailsText);
        Assert.Equal("A person who buys things.", browser.SummaryText);

        // The interface and the enum are described the same way.
        var priced = await TypeRowAsync(host, browser, "IPriced");
        SelectTypeRow(browser, priced);
        Assert.True(await WaitUntilAsync(host,
                () => browser.MemberRows.Any(row => row.Symbol.Name == "Price")),
            "selecting IPriced did not list its member Price.");
        Assert.Contains("Price()", browser.MemberRows.Single(row => row.Symbol.Name == "Price").Symbol.Signature);
        Assert.Equal("Something that has a price.", browser.SummaryText);

        var status = await TypeRowAsync(host, browser, "Status");
        SelectTypeRow(browser, status);
        Assert.True(await WaitUntilAsync(host, () => browser.SignatureText.Contains("Status", StringComparison.Ordinal)),
            "selecting the enum did not describe it.");
        Assert.Equal("The states of an order.", browser.SummaryText);

        // Search for a member name: the type that has it stays, its members narrow to it.
        SelectTypeRow(browser, customerRow);
        Assert.True(await WaitUntilAsync(host, () => browser.MemberRows.Count > 1),
            "Customer's members were not listed before the search.");

        browser.SearchBox.Text = "Total";

        Assert.True(await WaitUntilAsync(host,
                () => browser.MemberRows.Count == 1 && browser.MemberRows[0].Symbol.Name == "Total"),
            "searching for Total did not narrow the members to Total.");

        // Select the member: the description names its signature and summary.
        var total = browser.MemberRows.Single(row => row.Symbol.Name == "Total");
        browser.MemberList.SelectedItem = total;

        Assert.True(await WaitUntilAsync(host,
                () => browser.SignatureText == "Public Function Total(amount As Integer) As Integer"),
            "selecting Total did not show its signature.");
        Assert.Equal("Adds up the amounts of the purchases.", browser.SummaryText);

        // Double-click the member: the editor opens on its declaration line.
        browser.MemberList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        var expectedLine = LineOf(shopPath, "Public Function Total");

        Assert.True(await WaitUntilAsync(host,
                () => vm.ActiveDocument?.FilePath == shopPath),
            "double-clicking Total did not open the file that declares it.");

        var code = w.CodeEditorForTests(shopPath);
        Assert.NotNull(code);
        var editor = code!.GetVisualDescendants().OfType<TextEditor>().Single();

        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == expectedLine),
            $"the caret is on line {editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber}, not on Total's declaration at line {expectedLine}.");
    }

    // ------------------------------------------------------------------
    // Object Browser journey 2: a referenced framework type, System.String,
    // is browsed from the browser alone. Its namespace expands, the type is
    // selected and described, and double-clicking it opens no editor, since
    // there is no source of it to open.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AFrameworkTypeIsBrowsedWithoutOpeningAnEditor()
    {
        var project = WriteProject(_root, "Library", "Shop", ShopSource);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var browser = await OpenObjectBrowserAsync(host);

        var documentsBefore = vm.OpenDocuments.Count;
        var editorsBefore = w.GetVisualDescendants().OfType<CodeEditor>().Count();

        // "<All Libraries>" is the first entry and lists the framework too.
        browser.LibraryBox.SelectedIndex = 0;

        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserNamespaceRow>().Any(ns => ns.Name == "System")),
            "the framework namespace System is not listed.");

        await ExpandNamespaceAsync(host, browser, "System");

        var stringRow = await TypeRowAsync(host, browser, "String");
        SelectTypeRow(browser, stringRow);

        Assert.True(await WaitUntilAsync(host, () => browser.SignatureText.Contains("String", StringComparison.Ordinal)),
            "selecting System.String did not describe it.");
        Assert.Contains("Namespace System", browser.DetailsText);
        Assert.Contains(L(StringKeys.ObjectBrowserFromMetadata), browser.DetailsText, StringComparison.Ordinal);
        Assert.Contains(browser.MemberRows, row => row.Symbol.Name == "Length");

        browser.TypeList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(10);

        Assert.Equal(documentsBefore, vm.OpenDocuments.Count);
        Assert.Equal(editorsBefore, w.GetVisualDescendants().OfType<CodeEditor>().Count());
    }
}
