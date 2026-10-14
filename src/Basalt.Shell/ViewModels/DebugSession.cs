using Avalonia.Threading;
using Basalt.Core.Services;
using Basalt.Extensibility.Interpretation;
using Basalt.QuickBasic.Interpretation;
using Basalt.Workspace.Debugging;
using Basalt.Workspace.Immediate;
using Basalt.Core.Localization;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// Holds a debugging session together.
///
/// The breakpoints belong here rather than to the editor, because they outlive
/// any one open file: setting one, closing the file and starting a debug run
/// must still stop there. The panels read from this; the editor margin reports
/// into it.
/// </summary>
public sealed class DebugSession : IDisposable, IImmediateDebugger
{
    private readonly Dictionary<string, List<Breakpoint>> _breakpoints =
        new(StringComparer.Ordinal);

    private IDebugSessionService? _service;

    /// <summary>Whether a program is being debugged.</summary>
    public bool IsRunning => _service is { IsRunning: true };

    /// <summary>Whether the program is stopped.</summary>
    public bool IsPaused => _service?.IsPaused == true;

    /// <summary>Where execution is stopped, or null when it is running.</summary>
    public StackFrame? CurrentFrame { get; private set; }

    /// <summary>The exception the program is stopped on, "Type: message", or null when the stop is not one.</summary>
    public string? StoppedException => IsPaused ? _service?.StoppedException : null;

    /// <summary>The frame the user is looking at, which may not be the innermost.</summary>
    public int SelectedFrame { get; set; }

    public event EventHandler<StackFrame>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler<int>? Exited;
    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// What the program wrote through Debug and Trace, raised on the
    /// interface thread like the other events. The Immediate window listens
    /// here; <see cref="OutputReceived"/> still carries it as well.
    /// </summary>
    public event EventHandler<string>? DebugOutputReceived;

    /// <summary>The program is running, whether or not it will ever stop.</summary>
    public event EventHandler? Started;

    /// <summary>Raised when the breakpoints of a file change.</summary>
    public event EventHandler<string>? BreakpointsChanged;

    /// <summary>Raised when something failed, with a message worth showing.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Every breakpoint, across all files.</summary>
    public IReadOnlyList<Breakpoint> AllBreakpoints =>
        _breakpoints.Values.SelectMany(list => list).ToList();

    public IReadOnlyList<Breakpoint> BreakpointsIn(string filePath) =>
        _breakpoints.TryGetValue(filePath, out var list) ? list : [];

    /// <summary>
    /// Moves a file's breakpoints to a new path after Solution Explorer
    /// renames or moves it.
    ///
    /// Without this a breakpoint set before the rename keeps answering to a
    /// path that no longer exists, so it quietly stops working rather than
    /// following the file the way it does everywhere else in the IDE.
    /// </summary>
    public void RetargetBreakpoints(string oldPath, string newPath)
    {
        if (!_breakpoints.Remove(oldPath, out var list)) return;

        for (var i = 0; i < list.Count; i++)
            list[i] = list[i] with { FilePath = newPath };

        _breakpoints[newPath] = list;

        BreakpointsChanged?.Invoke(this, oldPath);
        BreakpointsChanged?.Invoke(this, newPath);
    }

    /// <summary>Sets or clears a breakpoint, reporting which it did.</summary>
    public async Task ToggleBreakpointAsync(
        string filePath, int line, CancellationToken ct = default)
    {
        if (!_breakpoints.TryGetValue(filePath, out var list))
        {
            list = [];
            _breakpoints[filePath] = list;
        }

        var existing = list.FindIndex(b => b.Line == line);

        if (existing >= 0) list.RemoveAt(existing);
        else list.Add(new Breakpoint(filePath, line));

        await PushAsync(filePath, ct).ConfigureAwait(true);
    }

    /// <summary>Switches a breakpoint on or off without removing it.</summary>
    public async Task SetBreakpointEnabledAsync(
        string filePath, int line, bool enabled, CancellationToken ct = default)
    {
        if (!_breakpoints.TryGetValue(filePath, out var list)) return;

        var index = list.FindIndex(b => b.Line == line);
        if (index < 0) return;

        list[index] = list[index] with { Enabled = enabled };

        await PushAsync(filePath, ct).ConfigureAwait(true);
    }

    /// <summary>Attaches a condition to a breakpoint, or removes one with null.</summary>
    public async Task SetBreakpointConditionAsync(
        string filePath, int line, string? condition, CancellationToken ct = default)
    {
        if (!_breakpoints.TryGetValue(filePath, out var list)) return;

        var index = list.FindIndex(b => b.Line == line);
        if (index < 0) return;

        list[index] = list[index] with { Condition = condition };

        await PushAsync(filePath, ct).ConfigureAwait(true);
    }

    /// <summary>Sets how many passes a breakpoint waits for, or none.</summary>
    public async Task SetBreakpointHitCountAsync(
        string filePath, int line, int? hitCount, CancellationToken ct = default)
    {
        if (!_breakpoints.TryGetValue(filePath, out var list)) return;

        var index = list.FindIndex(b => b.Line == line);
        if (index < 0) return;

        list[index] = list[index] with { HitCount = hitCount };

        await PushAsync(filePath, ct).ConfigureAwait(true);
    }

    private async Task PushAsync(string filePath, CancellationToken ct)
    {
        BreakpointsChanged?.Invoke(this, filePath);

        if (_service is null) return;

        try
        {
            await _service
                .SetBreakpointsAsync(filePath, BreakpointsIn(filePath), ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Failed?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Starts debugging.
    ///
    /// Which debugger is used follows what is being debugged: a .NET assembly
    /// goes to netcoredbg, and a QuickBASIC source file to the interpreter,
    /// which is the only way to step through one since netcoredbg speaks
    /// .NET and nothing else.
    ///
    /// Fails with a message rather than an exception when the debugger is not
    /// installed, since that is a setup problem the user can act on.
    /// </summary>
    public async Task StartAsync(
        string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(true);

        if (IsInterpreted(assemblyPath))
        {
            _service = CreateInterpreter(assemblyPath);
        }
        else
        {
            var adapter = DebugAdapterLocator.Find();

            if (adapter is null)
            {
                // Naming only what is missing leaves the user with a name and
                // nowhere to go. The three places that are looked in are the
                // three places a copy can be put, so they are the answer.
                Failed?.Invoke(this,
                    Localizer.Get(StringKeys.DebugAdapterNotFound,
                    Path.Combine("~", ".basalt", "debugger", DebugAdapterLocator.RuntimeIdentifier)));
                return;
            }

            _service = new NetCoreDebugService(adapter);
        }

        await StartWithAsync(_service, assemblyPath, workingDirectory, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Debugs a program already running, started by the IDE in its terminal so it can be typed to, and
    /// holding at its start until the debugger is attached (see DebuggerWaitHook).
    /// </summary>
    /// <param name="programPath">The program's assembly, for reporting its paths as the IDE spells them.</param>
    public async Task AttachAsync(int processId, string programPath, CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(true);

        var adapter = DebugAdapterLocator.Find();

        if (adapter is null)
        {
            Failed?.Invoke(this,
                Localizer.Get(StringKeys.DebugAdapterNotFound,
                Path.Combine("~", ".basalt", "debugger", DebugAdapterLocator.RuntimeIdentifier)));
            return;
        }

        var service = new NetCoreDebugService(adapter);

        await StartWithAsync(service, (s, token) => ((NetCoreDebugService)s).AttachAsync(processId, programPath, token), ct)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Connects a service to the session's events and launches the program with it.
    ///
    /// Apart from <see cref="StartAsync"/> so that a stand-in for the debug
    /// adapter can be run through the same wiring the real one gets.
    /// </summary>
    internal Task StartWithAsync(
        IDebugSessionService service, string assemblyPath, string? workingDirectory, CancellationToken ct = default) =>
        StartWithAsync(service, (s, token) => s.LaunchAsync(assemblyPath, workingDirectory, token), ct);

    /// <summary>Connects a service to the session's events, sends the breakpoints and starts it the given way.</summary>
    private async Task StartWithAsync(
        IDebugSessionService service, Func<IDebugSessionService, CancellationToken, Task> start, CancellationToken ct)
    {
        _service = service;

        // Every one of these arrives on the reader thread that pumps the
        // debug adapter, and every subscriber is a control. Touching Avalonia
        // from there does nothing visible on macOS rather than failing
        // loudly, which is how a breakpoint could be hit — the debugger
        // reported it correctly — while the window sat on "starting".
        //
        // Marshalled once here rather than in each subscriber: this is the
        // edge where the background thread ends, and a handler added later
        // would otherwise have to remember on its own.
        _service.Paused += (_, frame) => OnUiThread(() => OnPaused(this, frame));
        _service.Resumed += (_, _) => OnUiThread(() => OnResumed(this, EventArgs.Empty));
        _service.Exited += (_, code) => OnUiThread(() => OnExited(this, code));
        _service.OutputReceived += (_, text) => OnUiThread(() => OutputReceived?.Invoke(this, text));
        _service.Started += (_, _) => OnUiThread(() => Started?.Invoke(this, EventArgs.Empty));

        if (_service is NetCoreDebugService netCore)
            netCore.DebugOutputReceived += (_, text) => OnUiThread(() => DebugOutputReceived?.Invoke(this, text));
        _service.Faulted += (_, ex) => OnUiThread(() => Failed?.Invoke(this, ex.Message));

        // Not marshalled: the interpreter blocks inside a statement waiting
        // for the answer, so posting to the interface thread and returning
        // would answer with nothing typed. The handler moves itself.
        if (_service is InterpreterDebugService interpreted)
            interpreted.InputRequested += (_, request) => InputRequested?.Invoke(this, request);

        foreach (var (file, list) in _breakpoints)
            await _service.SetBreakpointsAsync(file, list, ct).ConfigureAwait(true);

        try
        {
            await start(_service, ct).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Failed?.Invoke(this, ex.Message);
            await StopAsync(ct).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether a file is one the interpreter runs.
    ///
    /// By extension rather than by asking a registry: what is being started
    /// is a path, and the answer has to be known before anything is loaded.
    /// </summary>
    private static bool IsInterpreted(string path) =>
        Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase);

    /// <summary>The interpreter for a file, behind the debug service.</summary>
    private static IDebugSessionService CreateInterpreter(string path) =>
        new InterpreterDebugService(new QuickBasicInterpreter());

    /// <summary>
    /// Changes a variable while the program is stopped.
    ///
    /// Only the interpreter offers this so far; netcoredbg would need a
    /// setVariable request, which is not written yet, so it says no rather
    /// than pretending to have done it.
    /// </summary>
    public bool SetVariable(string name, string value) =>
        _service is InterpreterDebugService interpreted
        && IsPaused
        && interpreted.SetVariable(name, value, SelectedFrame);

    /// <summary>
    /// The program is waiting for a line to be typed.
    ///
    /// Only an interpreted program asks: a compiled one has its own console.
    /// </summary>
    public event EventHandler<InputRequest>? InputRequested;

    /// <summary>
    /// Runs something on the interface thread, where there is one.
    ///
    /// Posted rather than invoked and waited for: the reader thread has to
    /// get back to the adapter, and blocking it while the interface works
    /// would stall the very messages the interface is about to ask for.
    ///
    /// Where no interface is running the work is done on the spot. Posting
    /// it would queue it against a dispatcher that nothing is pumping, and
    /// the event would simply never arrive — which is the same silence this
    /// method exists to cure, only moved.
    /// </summary>
    private void OnUiThread(Action work)
    {
        if (MarshalToInterface && !Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(work);
        else
            work();
    }

    /// <summary>
    /// Whether events should be handed to the interface thread.
    /// </summary>
    /// <remarks>
    /// Set by the window that owns the session, rather than guessed from the
    /// thread that built it. Guessing was wrong twice over: a dispatcher
    /// exists in the tests too but nothing pumps it, so posted work is never
    /// run, and whether the constructing thread happens to be the interface
    /// one depends on what ran before.
    ///
    /// Off by default, which is the case that always works — the events are
    /// raised where they arrive. The window turns it on because only there
    /// is a dispatcher actually running.
    /// </remarks>
    public bool MarshalToInterface { get; set; }

    private void OnPaused(object? sender, StackFrame frame)
    {
        CurrentFrame = frame;
        SelectedFrame = 0;
        Paused?.Invoke(this, frame);
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        CurrentFrame = null;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    private void OnExited(object? sender, int code)
    {
        CurrentFrame = null;
        Exited?.Invoke(this, code);
    }

    public Task ContinueAsync(CancellationToken ct = default)
    {
        // Said before the program runs on: what the user talks to next is the program, not the panels.
        if (_service is not null) Continuing?.Invoke(this, EventArgs.Empty);

        return _service?.ContinueAsync(ct) ?? Task.CompletedTask;
    }

    /// <summary>The user asked the program to run on (Continue), as opposed to a step that stops again at once.</summary>
    public event EventHandler? Continuing;

    /// <summary>
    /// Breaks into a running program.
    ///
    /// Only worth offering where the debugger can do it: an interpreted
    /// program has no way to interrupt itself, so <see cref="CanPause"/>
    /// says so and the button is greyed rather than doing nothing.
    /// </summary>
    public Task PauseAsync(CancellationToken ct = default) =>
        CanPause ? _service!.PauseAsync(ct) : Task.CompletedTask;

    /// <summary>Whether a running program can be broken into.</summary>
    public bool CanPause =>
        _service is NetCoreDebugService && IsRunning && !IsPaused;

    public Task StepOverAsync(CancellationToken ct = default) =>
        _service?.StepOverAsync(ct) ?? Task.CompletedTask;

    public Task StepIntoAsync(CancellationToken ct = default) =>
        _service?.StepIntoAsync(ct) ?? Task.CompletedTask;

    public Task StepOutAsync(CancellationToken ct = default) =>
        _service?.StepOutAsync(ct) ?? Task.CompletedTask;

    public Task<IReadOnlyList<StackFrame>> GetCallStackAsync(CancellationToken ct = default) =>
        _service?.GetCallStackAsync(ct) ?? Task.FromResult<IReadOnlyList<StackFrame>>([]);

    public Task<IReadOnlyList<VariableValue>> GetLocalsAsync(CancellationToken ct = default) =>
        _service?.GetLocalsAsync(SelectedFrame, ct)
        ?? Task.FromResult<IReadOnlyList<VariableValue>>([]);

    public Task<IReadOnlyList<VariableValue>> ExpandAsync(
        int reference, CancellationToken ct = default) =>
        _service?.ExpandAsync(reference, ct)
        ?? Task.FromResult<IReadOnlyList<VariableValue>>([]);

    /// <summary>
    /// Evaluates an expression in the frame being looked at.
    ///
    /// Returns null when nothing is stopped, so callers such as the hover
    /// tooltip can simply show nothing.
    /// </summary>
    public Task<string?> EvaluateAsync(string expression, CancellationToken ct = default) =>
        _service is null || !IsPaused
            ? Task.FromResult<string?>(null)
            : _service.EvaluateAsync(expression, SelectedFrame, ct);

    /// <inheritdoc />
    bool IImmediateDebugger.IsDebugging => IsRunning;

    /// <summary>
    /// Evaluates a line from the Immediate window in the frame being looked at.
    ///
    /// Refuses, with an empty failure, when nothing is stopped. The service is
    /// read once: the session can end on another thread between the check and
    /// the call.
    /// </summary>
    public Task<ReplEvaluation> EvaluateReplAsync(
        string text, bool isExpression, CancellationToken ct = default)
    {
        var service = _service;

        return service is null || !service.IsPaused
            ? Task.FromResult(new ReplEvaluation(false, ""))
            : service.EvaluateReplAsync(text, isExpression, SelectedFrame, ct);
    }

    Task<ReplEvaluation> IImmediateDebugger.EvaluateAsync(
        string text, bool isExpression, CancellationToken ct) =>
        EvaluateReplAsync(text, isExpression, ct);

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_service is null) return;

        await _service.StopAsync(ct).ConfigureAwait(true);

        _service.Dispose();
        _service = null;
        CurrentFrame = null;
    }

    public void Dispose()
    {
        _service?.Dispose();
        _service = null;
    }
}
