using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// New Solution > QuickBASIC, then running what it made, the way a person
/// does it: a real <see cref="MainWindow"/> opened on the template's file,
/// and Run > Run Without Debugging.
/// </summary>
/// <remarks>
/// The template's solution path is the .bas file itself. Before the fix the
/// shell handed that file to Roslyn as a project, and Roslyn refused it with
/// "not associated with a language": the error went to Output, and Run had
/// no project to run, so it printed nothing.
///
/// Red before the fix: the first assertion failed with the Roslyn error in
/// Output. Reintroducing only the Run defect (no project, so return without
/// running the file) failed the last assertion with "Run Without Debugging
/// printed nothing".
/// </remarks>
public sealed class UserJourneyQuickBasicTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-quickbasic-").FullName;

    /// <summary>The native program a test started, so a failing test does not leave it running.</summary>
    private int? _programProcessId;

    public void Dispose()
    {
        if (_programProcessId is { } processId) KillIfAlive(processId);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string L(string key) => Localizer.Get(key);

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 150)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    /// <summary>
    /// The template opens as the solution the New Solution command opens, with
    /// no error in Output, and Run Without Debugging compiles it and prints
    /// what the program prints.
    /// </summary>
    [AvaloniaFact]
    public async Task ANewQuickBasicSolutionOpensWithoutErrorAndRunsItsProgram()
    {
        // The program is compiled natively by clang, the same as the other
        // QuickBASIC tests; a machine without clang says so and skips.
        Clang.SkipUnlessInstalled();

        var created = await SolutionTemplates.CreateAsync(
            _root, "Squares", ProjectTemplate.QuickBasic, TestContext.Current.CancellationToken);

        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => vm.SolutionPath == created.SolutionPath && !vm.IsBusy, maxPasses: 400),
            "the template never finished opening. Output:\n" + string.Join("\n", vm.BuildOutput));

        Assert.DoesNotContain(vm.BuildOutput, line => line.Contains("[error]", StringComparison.Ordinal));
        Assert.Equal(created.SolutionPath, vm.ActiveDocument?.FilePath);

        InvokeMenu(w, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        var printed = await WaitUntilAsync(host,
            () => vm.BuildOutput.Any(line => line.Contains("The sum of the first five squares is", StringComparison.Ordinal))
               && vm.BuildOutput.Any(line => line.Trim() == "55"),
            maxPasses: 600);

        Assert.True(printed, "Run Without Debugging printed nothing. Output:\n" + string.Join("\n", vm.BuildOutput));
        Assert.DoesNotContain(vm.BuildOutput, line => line.Contains("[error]", StringComparison.Ordinal));
    }

    /// <summary>
    /// Closing the IDE while a compiled program is still running ends it. The
    /// program is started by the window and nothing else ends it, so without
    /// this it outlives the IDE.
    /// </summary>
    /// <remarks>
    /// Red before the fix: with the StopSourceFileRun() call removed from
    /// MainWindowViewModel.Dispose, the test failed with "the program (process
    /// N) is still running after its window closed" after the 10 second wait.
    /// Restored, it passes in about 2 seconds, and the program is gone.
    ///
    /// Not covered by a test: the Win32Exception catch in StopSourceFileRun and
    /// the disposal of the replaced token source in DotnetTestService.StopAll.
    /// The first needs the OS to refuse a kill; the second leaves no observable
    /// trace. The token source was checked in a scratch program instead: a
    /// token captured before the source is cancelled and disposed still links.
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingTheWindowEndsAQuickBasicProgramThatIsStillRunning()
    {
        Clang.SkipUnlessInstalled();

        var created = await SolutionTemplates.CreateAsync(
            _root, "Spin", ProjectTemplate.QuickBasic, TestContext.Current.CancellationToken);

        // Loops for ever, so the program can only end when the window does.
        await File.WriteAllTextAsync(created.SolutionPath, """
            DIM n AS LONG
            n = 0
            DO
                n = n + 1
            LOOP
            """, TestContext.Current.CancellationToken);

        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => vm.SolutionPath == created.SolutionPath && !vm.IsBusy, maxPasses: 400),
            "the template never finished opening. Output:\n" + string.Join("\n", vm.BuildOutput));

        InvokeMenu(w, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        var started = await WaitUntilAsync(host,
            () => vm.SourceFileProcessIdForTests is not null, maxPasses: 600);

        Assert.True(started, "Run Without Debugging never started the program. Output:\n" + string.Join("\n", vm.BuildOutput));

        var processId = vm.SourceFileProcessIdForTests!.Value;
        _programProcessId = processId;

        // Without this the closing check below could pass for a program that
        // had already died on its own.
        Assert.True(IsAlive(processId), $"the program (process {processId}) was not running before the window closed.");

        w.Close();

        await AssertGoneAsync(processId);
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asserts the program is gone. A process killed a moment ago can take a
    /// short while to be reaped, so the check allows a brief wait; still
    /// running after that is a leak.
    /// </summary>
    private static async Task AssertGoneAsync(int processId)
    {
        var clock = Stopwatch.StartNew();

        while (IsAlive(processId) && clock.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50);

        Assert.False(IsAlive(processId),
            $"the program (process {processId}) is still running after its window closed.");
    }

    private static void KillIfAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
