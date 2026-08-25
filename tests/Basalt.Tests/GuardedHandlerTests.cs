using Avalonia.Headless.XUnit;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Work started from an event handler that goes wrong.
///
/// There were forty "async void" handlers in the main window. An exception in
/// any of them had nowhere to go: it reached the runtime and took the
/// application down. Pressing Start Debug went through one of them.
/// </summary>
public class GuardedHandlerTests
{
    [Fact]
    public async Task ReportsWhatTheWorkThrewInsteadOfCrashing()
    {
        var said = new List<string>();

        Guarded.Run(() => throw new InvalidOperationException("the adapter said no"),
            said.Add, "debug");

        // The handler returns straight away; the report arrives with the task.
        await Task.Delay(50);

        var message = Assert.Single(said);

        Assert.Contains("the adapter said no", message, StringComparison.Ordinal);
        Assert.Contains("[debug]", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysNothingWhenTheWorkSucceeds()
    {
        var said = new List<string>();

        Guarded.Run(() => Task.CompletedTask, said.Add, "ide");

        await Task.Delay(50);

        Assert.Empty(said);
    }

    [Fact]
    public async Task TreatsCancellationAsNormal()
    {
        // A panel closed while it was still loading cancels its work. Saying
        // so on every close would be noise.
        var said = new List<string>();

        Guarded.Run(() => Task.FromCanceled(new CancellationToken(canceled: true)),
            said.Add, "ide");

        await Task.Delay(50);

        Assert.Empty(said);
    }

    [Fact]
    public async Task ReportsSomethingThrownBeforeTheFirstAwait()
    {
        // Thrown while building the task rather than inside it: without the
        // try around the call itself this one still escaped.
        var said = new List<string>();

        Guarded.Run(
            () => throw new InvalidOperationException("thrown at once"),
            said.Add, "ide");

        await Task.Delay(50);

        Assert.Single(said);
    }
}

/// <summary>
/// Starting a debugging session in every state the IDE can be in.
///
/// None of them may end with the application disappearing: a line in the
/// output pane is always the better answer.
/// </summary>
public class DebugStartTests
{
    [AvaloniaFact]
    public async Task StartingWithNoSolutionOpenSaysSoAndSurvives()
    {
        using var window = new TestWindow();
        await window.SettleAsync();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        var assembly = await vm.BuildForDebuggingAsync();

        Assert.Null(assembly);

        // Still alive, and it said why.
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [AvaloniaFact]
    public async Task StartingWithoutADebuggerInstalledReportsIt()
    {
        var session = new DebugSession();

        var failures = new List<string>();

        session.Failed += (_, message) => failures.Add(message);

        var root = Directory.CreateTempSubdirectory("basalt-debugstart-").FullName;

        try
        {
            // A path that is not a .bas, so the real adapter is looked for.
            var assembly = Path.Combine(root, "Nothing.dll");

            await File.WriteAllTextAsync(assembly, "");

            await session.StartAsync(assembly, root);

            // Either it found a debugger or it said it could not. Never a crash.
            Assert.True(failures.Count > 0 || session.IsRunning);
        }
        finally
        {
            await session.StopAsync();

            try { Directory.Delete(root, true); } catch { }
        }
    }
}
