using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Basalt.Core.Commands;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using Basalt.Workspace.Immediate;

namespace Basalt.Tests;

/// <summary>The Immediate window panel: transcript, history keys, Clear, debug output.</summary>
public sealed class ImmediatePanelTests
{
    private sealed class EchoEvaluator : IImmediateEvaluator
    {
        public Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct = default) =>
            Task.FromResult(input.Text == "bad"
                ? new ImmediateOutcome(ImmediateOutcomeKind.CompileError, "BC30451: 'bad' is not declared.")
                : new ImmediateOutcome(ImmediateOutcomeKind.Output, "=> " + input.Text));
    }

    private static ImmediatePanel NewPanel(DebugSession? debug = null) =>
        new(new ImmediateSession(debug, new EchoEvaluator()), debug);

    private static void Press(TextBox box, Key key) =>
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public async Task AnEnteredLineIsEchoedAndAnswered()
    {
        var panel = NewPanel();
        var window = new Window { Width = 500, Height = 300, Content = panel };
        window.Show();

        try
        {
            panel.Input.Text = "?1+2";
            await panel.SubmitAsync();

            Assert.Equal(["> ?1+2", "=> 1+2"], panel.TranscriptLines);
            Assert.Equal("", panel.Input.Text);
        }
        finally { window.Close(); }
    }

    private sealed class WaitingEvaluator : IImmediateEvaluator
    {
        public TaskCompletionSource Started { get; } = new();

        public async Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);

            return new ImmediateOutcome(ImmediateOutcomeKind.Output, "never");
        }
    }

    [AvaloniaFact]
    public async Task EscapeStopsALineThatIsStillRunning()
    {
        var waiting = new WaitingEvaluator();
        var panel = new ImmediatePanel(new ImmediateSession(null, waiting), null);
        var window = new Window { Width = 500, Height = 300, Content = panel };
        window.Show();

        try
        {
            panel.Input.Text = "?forever";
            var submit = panel.SubmitAsync();
            await waiting.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Press(panel.Input, Key.Escape);
            await submit.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(["> ?forever", "Stopped."], panel.TranscriptLines);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LeavingTheVisualTreeDoesNotStopARunningLineButClosingTheWindowDoes()
    {
        var waiting = new WaitingEvaluator();
        var panel = new ImmediatePanel(new ImmediateSession(null, waiting), null);
        var window = new Window { Width = 500, Height = 300, Content = panel };
        window.Show();

        panel.Input.Text = "?forever";
        var submit = panel.SubmitAsync();
        await waiting.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // What Dock does to a tab that stops being the active one.
        window.Content = null;
        await Task.Delay(200);
        Assert.False(submit.IsCompleted, "Switching tabs stopped the line.");

        window.Content = panel;
        window.Close();
        await submit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("Stopped.", panel.TranscriptLines);
    }

    [AvaloniaFact]
    public async Task UpAndDownWalkTheHistoryInTheInputBox()
    {
        var panel = NewPanel();
        var window = new Window { Width = 500, Height = 300, Content = panel };
        window.Show();

        try
        {
            foreach (var line in new[] { "?1", "?2" })
            {
                panel.Input.Text = line;
                await panel.SubmitAsync();
            }

            Press(panel.Input, Key.Up);
            Assert.Equal("?2", panel.Input.Text);
            Press(panel.Input, Key.Up);
            Assert.Equal("?1", panel.Input.Text);
            Press(panel.Input, Key.Down);
            Assert.Equal("?2", panel.Input.Text);
            Press(panel.Input, Key.Down);
            Assert.Equal("", panel.Input.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ErrorsAreMarkedAndClearEmptiesTheTranscript()
    {
        var panel = NewPanel();
        var window = new Window { Width = 500, Height = 300, Content = panel };
        window.Show();

        try
        {
            panel.Input.Text = "?bad";
            await panel.SubmitAsync();

            Assert.Contains("BC30451: 'bad' is not declared.", panel.TranscriptLines);

            panel.Clear();
            Assert.Empty(panel.TranscriptLines);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DebugPrintOutputOfTheDebuggedProgramAppearsInThePanel()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        using var debug = new DebugSession();
        var panel = NewPanel(debug);

        await debug.StartWithAsync(service, "/app/App.dll", null);

        dap.Raise("output", new JsonObject { ["category"] = "console", ["output"] = "Debug.Print: total = 42\n" });
        dap.Raise("output", new JsonObject { ["category"] = "stdout", ["output"] = "plain console text\n" });

        Assert.Equal(["Debug.Print: total = 42"], panel.TranscriptLines);
    }

    [Fact]
    public void TheCommandHasVisualStudiosGestureAndDoesNotStealGoToLinesShortcut()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);

        Assert.NotNull(registry.ById(IdeCommands.ViewImmediate));
        Assert.Equal("Ctrl+Alt+I", registry.GestureFor(IdeCommands.ViewImmediate));
        Assert.Equal("Ctrl+G", registry.GestureFor(IdeCommands.NavigateGoToLine));
    }
}
