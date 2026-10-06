using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Immediate;

namespace Basalt.Shell.Controls;

/// <summary>
/// The Immediate window of Visual Basic 6: type a line, press Enter, see what
/// it came to.
///
/// A transcript above and a line to type in below. "?x" or "Print x" prints a
/// value, anything else runs as a statement. Up and Down walk the lines typed
/// before. Stopped at a break the line is evaluated in the paused frame;
/// otherwise it is compiled and run in a separate process (see
/// <see cref="ImmediateSession"/>). What the debugged program writes through
/// Debug.Print and Debug.WriteLine arrives here too, as it did in VB6.
/// </summary>
public sealed class ImmediatePanel : UserControl
{
    /// <summary>Lines kept in the transcript; the oldest go first.</summary>
    internal const int TranscriptLimit = 2000;

    private readonly ImmediateSession _session;
    private readonly StackPanel _transcript = new();
    private readonly ScrollViewer _scroller;
    private readonly TextBox _input;
    private FontFamily _font;
    private bool _scrollPending;
    private CancellationTokenSource? _running;

    public ImmediatePanel(MainWindowViewModel viewModel, DebugSession debug)
        : this(
            new ImmediateSession(
                debug,
                new ImmediateEvaluator(viewModel.ResolveImmediateTargetAsync)),
            debug,
            viewModel.LoadSettings?.Invoke().Editor.FontFamily)
    {
    }

    internal ImmediatePanel(ImmediateSession session, DebugSession? debug, string? fontFamily = null)
    {
        _session = session;
        _font = new FontFamily(fontFamily ?? new EditorSettings().FontFamily);

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(Spacing.Normal, Spacing.Tight),
                Child = _transcript
            },
            FontFamily = _font
        };

        _input = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.ImmediatePrompt),
            FontFamily = _font,
            FontSize = PanelMetrics.FontSize,
            AcceptsReturn = false,
            Margin = new Thickness(Spacing.Tight)
        };

        _input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var clear = PanelToolbar.IconButton(IconKind.Close, Localizer.Get(StringKeys.ImmediateClear), Clear);
        var stop = PanelToolbar.IconButton(IconKind.Stop, Localizer.Get(StringKeys.ImmediateStop), () => Cancel());

        DockPanel.SetDock(_input, Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Children =
            {
                PanelToolbar.Row(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { clear, stop }
                }),
                _input,
                _scroller
            }
        };

        // A line still running when its window closes is stopped rather than
        // orphaned (the IDE exiting is covered by ChildProcessRegistry). Not
        // when the panel merely leaves the visual tree: Dock detaches every
        // tab that is not the active one, and switching to another panel
        // must not kill the line the user is waiting for.
        AttachedToVisualTree += (_, _) => WatchWindow(TopLevel.GetTopLevel(this) as Window);
        DetachedFromVisualTree += (_, _) => WatchWindow(null);

        // Already on the interface thread: the session marshals its events.
        if (debug is not null) debug.DebugOutputReceived += (_, text) => AppendText(text, "outputLine");
    }

    /// <summary>The line being typed.</summary>
    internal TextBox Input => _input;

    /// <summary>What the transcript says, one entry per line, for the tests.</summary>
    internal IReadOnlyList<string> TranscriptLines =>
        [.. _transcript.Children.OfType<SelectableTextBlock>().Select(b => b.Text ?? "")];

    /// <summary>
    /// Evaluates the line in the box, shows what it came to and returns when
    /// it has. Resumes on the interface thread because it is awaited there;
    /// the evaluation itself ran on the thread pool.
    /// </summary>
    internal async Task SubmitAsync()
    {
        var line = _input.Text ?? "";

        // One line at a time. The box stays enabled while it runs, because a
        // disabled box cannot hear Esc, which is how a line is stopped.
        if (line.Trim().Length == 0 || _running is not null) return;

        _input.Text = "";

        AppendText("> " + line.Trim(), "outputLine", "immediateEcho");

        using var running = new CancellationTokenSource();
        _running = running;

        try
        {
            var outcome = await _session.SubmitAsync(line, running.Token).ConfigureAwait(true);

            Show(outcome);
        }
        catch (OperationCanceledException)
        {
            AppendText(Localizer.Get(StringKeys.ImmediateStopped), "outputLine", "warning");
        }
        catch (Exception ex)
        {
            // A failure here is the evaluator's, not the line's; it must not
            // leave the box disabled.
            AppendText(ex.Message, "outputLine", "error");
        }
        finally
        {
            _running = null;
            _input.Focus();
        }
    }

    private void Show(ImmediateOutcome outcome)
    {
        if (outcome.Text.Length == 0) return;

        var kind = outcome.IsProblem ? "error" : null;

        foreach (var text in outcome.Text.Split('\n'))
            AppendText(text.TrimEnd('\r'), "outputLine", kind);
    }

    /// <summary>Appends program output, which may carry several lines and a trailing newline.</summary>
    private void AppendText(string text, string className, string? kind = null)
    {
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            // A trailing newline ends the last line rather than starting an empty one.
            if (i == lines.Length - 1 && lines[i].Length == 0 && lines.Length > 1) break;

            var block = new SelectableTextBlock { Text = lines[i].TrimEnd('\r') };
            block.Classes.Add(className);
            if (kind is not null) block.Classes.Add(kind);

            _transcript.Children.Add(block);
        }

        while (_transcript.Children.Count > TranscriptLimit) _transcript.Children.RemoveAt(0);

        ScrollToEnd();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = SubmitAsync();
                break;

            case Key.Escape:
                e.Handled = Cancel();
                break;

            case Key.Up:
                e.Handled = true;
                if (_session.History.Previous() is { } older) SetInput(older);
                break;

            case Key.Down:
                e.Handled = true;
                if (_session.History.Next() is { } newer) SetInput(newer);
                break;
        }
    }

    private Window? _watched;

    private void WatchWindow(Window? window)
    {
        if (_watched is not null) _watched.Closing -= OnWindowClosing;

        _watched = window;

        if (window is not null) window.Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e) => Cancel();

    /// <summary>Stops the line that is running, building or waiting; false when none is.</summary>
    internal bool Cancel()
    {
        var running = _running;

        if (running is null) return false;

        try
        {
            running.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    private void SetInput(string text)
    {
        _input.Text = text;
        _input.CaretIndex = text.Length;
    }

    /// <summary>Empties the transcript; the history of typed lines is kept.</summary>
    internal void Clear()
    {
        _transcript.Children.Clear();
        _input.Focus();
    }

    /// <summary>Follows the editor's font when it changes in Settings.</summary>
    public void ApplySettings(IdeSettings settings)
    {
        _font = new FontFamily(settings.Editor.FontFamily);
        _scroller.FontFamily = _font;
        _input.FontFamily = _font;
    }

    private void ScrollToEnd()
    {
        if (_scrollPending) return;

        _scrollPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            _scroller.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
