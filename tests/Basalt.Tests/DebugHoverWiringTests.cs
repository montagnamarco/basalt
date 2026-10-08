using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Resting the pointer on a variable while stopped in the debugger shows its value, as Visual
/// Studio's data tips do. The evaluator existed but no real editor was ever connected to it, so in
/// the running IDE the hover showed nothing; these drive the real window with a real pointer.
/// </summary>
public sealed class DebugHoverWiringTests
{
    private const string Program = "Module Program\n    Sub Main()\n        Dim counter = 42\n    End Sub\nEnd Module\n";

    [AvaloniaFact]
    public async Task RestingOnAVariableWhileStoppedShowsItsValue()
    {
        var (host, path) = await OpenProgramAsync();
        using (host)
        {
            var debug = DebugSessionOf(host.Window);
            var fake = new EvaluatingDebugService();
            await debug.StartWithAsync(fake, "fake.dll", null);
            fake.RaisePaused();
            await host.SettleAsync(2);

            var evaluator = host.Window.HoverEvaluatorForTests(path);
            Assert.NotNull(evaluator);

            MovePointerOver(host.Window, "counter");
            await WaitUntilAsync(() => evaluator!.Showing == "counter" && evaluator.DisplayedText.Length > 0);

            Assert.Equal("counter = 42", evaluator!.DisplayedText);
            Assert.Equal("counter", Assert.Single(fake.Evaluated));

            await debug.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task ContinuingOrScrollingClosesTheTipWithoutMovingThePointer()
    {
        var (host, path) = await OpenProgramAsync();
        using (host)
        {
            var debug = DebugSessionOf(host.Window);
            var fake = new EvaluatingDebugService();
            await debug.StartWithAsync(fake, "fake.dll", null);
            fake.RaisePaused();
            await host.SettleAsync(2);
            var tip = host.Window.HoverEvaluatorForTests(path)!;

            MovePointerOver(host.Window, "counter");
            await WaitUntilAsync(() => tip.Showing == "counter");
            fake.RaiseResumed();
            await host.SettleAsync(1);
            Assert.Null(tip.Showing);

            fake.RaisePaused();
            await host.SettleAsync(1);
            MovePointerOver(host.Window, "counter", nudge: 2);
            await WaitUntilAsync(() => tip.Showing == "counter");
            var view = tip.Editor.TextArea.TextView;
            tip.Editor.Document.Insert(tip.Editor.Document.TextLength, string.Concat(Enumerable.Repeat("' filler\n", 200)));
            await host.SettleAsync(2);
            Assert.Equal("counter", tip.Showing);
            tip.Editor.ScrollToLine(150);
            await host.SettleAsync(2);
            Assert.True(view.ScrollOffset.Y > 0, "the editor did not scroll.");
            Assert.Null(tip.Showing);

            await debug.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task TheSymbolTooltipYieldsToTheDataTipOnlyWhileStopped()
    {
        var (host, _) = await OpenProgramAsync();
        using (host)
        {
            var editor = Assert.Single(host.Window.GetVisualDescendants().OfType<CodeEditor>());
            var debug = DebugSessionOf(host.Window);
            var fake = new EvaluatingDebugService();

            Assert.False(editor.HoverYieldsToDebugger?.Invoke() == true, "with no session the symbol tooltip must still answer.");

            await debug.StartWithAsync(fake, "fake.dll", null);
            fake.RaisePaused();
            await host.SettleAsync(2);
            Assert.True(editor.HoverYieldsToDebugger?.Invoke() == true, "while stopped only the data tip may answer.");

            fake.RaiseResumed();
            await host.SettleAsync(2);
            Assert.False(editor.HoverYieldsToDebugger?.Invoke() == true, "once running again the symbol tooltip answers again.");

            await debug.StopAsync();
        }
    }

    private static async Task<(TestWindow Host, string Path)> OpenProgramAsync()
    {
        var root = Directory.CreateTempSubdirectory("basalt-debughover-").FullName;
        var project = Path.Combine(root, "Hover.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        var path = Path.Combine(root, "Program.vb");
        File.WriteAllText(path, Program);

        var host = new TestWindow();
        await host.SettleAsync(2);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        await viewModel.OpenSolutionAsync(project);
        await viewModel.OpenFileAsync(path);
        await host.SettleAsync(4);
        return (host, path);
    }

    /// <summary>Moves the real pointer onto the middle of a word, through the window like a mouse.</summary>
    private static void MovePointerOver(MainWindow window, string word, int nudge = 0)
    {
        var editor = Assert.Single(window.GetVisualDescendants().OfType<TextEditor>(),
            candidate => candidate.Document?.Text == Program);
        var offset = editor.Document.Text.IndexOf(word, StringComparison.Ordinal) + word.Length / 2;
        var location = editor.Document.GetLocation(offset);
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        var inView = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineMiddle) - view.ScrollOffset;
        var inWindow = view.TranslatePoint(inView, window)!.Value;

        window.MouseMove(inWindow + new Vector(nudge, 1));
        window.MouseMove(inWindow + new Vector(nudge, 0));
        Dispatcher.UIThread.RunJobs();
    }

    private static DebugSession DebugSessionOf(MainWindow window) =>
        (DebugSession)typeof(MainWindow)
            .GetField("_debug", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;


    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the data tip never showed the value.");
    }

    /// <summary>A stand-in for netcoredbg that answers every evaluation with 42.</summary>
    private sealed class EvaluatingDebugService : IDebugSessionService
    {
        public List<string> Evaluated { get; } = [];

        public bool IsRunning { get; private set; } = true;
        public bool IsPaused { get; private set; }

        public event EventHandler<StackFrame>? Paused;
        public event EventHandler? Resumed;
        public event EventHandler<int>? Exited;
        public event EventHandler<string>? OutputReceived { add { } remove { } }
        public event EventHandler? Started;
        public event EventHandler<Exception>? Faulted { add { } remove { } }

        public void RaisePaused()
        {
            IsPaused = true;
            Paused?.Invoke(this, new StackFrame("Main", null, 3));
        }

        public void RaiseResumed()
        {
            IsPaused = false;
            Resumed?.Invoke(this, EventArgs.Empty);
        }

        public Task LaunchAsync(string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
        {
            Started?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task AttachAsync(int processId, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBreakpointsAsync(string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ContinueAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<StackFrame>> GetCallStackAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StackFrame>>([]);

        public Task<IReadOnlyList<VariableValue>> GetLocalsAsync(int frameIndex, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VariableValue>>([]);

        public Task<string?> EvaluateAsync(string expression, int frameIndex, CancellationToken ct = default)
        {
            Evaluated.Add(expression);
            return Task.FromResult<string?>("42");
        }

        public Task<IReadOnlyList<VariableValue>> ExpandAsync(int reference, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VariableValue>>([]);

        public Task StopAsync(CancellationToken ct = default)
        {
            IsRunning = false;
            Exited?.Invoke(this, 0);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
