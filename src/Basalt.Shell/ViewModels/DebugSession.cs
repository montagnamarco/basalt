using Basalt.Core.Services;
using Basalt.Extensibility.Interpretation;
using Basalt.QuickBasic.Interpretation;
using Basalt.Workspace.Debugging;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// Holds a debugging session together.
///
/// The breakpoints belong here rather than to the editor, because they outlive
/// any one open file: setting one, closing the file and starting a debug run
/// must still stop there. The panels read from this; the editor margin reports
/// into it.
/// </summary>
public sealed class DebugSession : IDisposable
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

    /// <summary>The frame the user is looking at, which may not be the innermost.</summary>
    public int SelectedFrame { get; set; }

    public event EventHandler<StackFrame>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler<int>? Exited;
    public event EventHandler<string>? OutputReceived;

    /// <summary>Raised when the breakpoints of a file change.</summary>
    public event EventHandler<string>? BreakpointsChanged;

    /// <summary>Raised when something failed, with a message worth showing.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Every breakpoint, across all files.</summary>
    public IReadOnlyList<Breakpoint> AllBreakpoints =>
        _breakpoints.Values.SelectMany(list => list).ToList();

    public IReadOnlyList<Breakpoint> BreakpointsIn(string filePath) =>
        _breakpoints.TryGetValue(filePath, out var list) ? list : [];

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
                Failed?.Invoke(this,
                    "The debugger (netcoredbg) was not found. Install it to debug.");
                return;
            }

            _service = new NetCoreDebugService(adapter);
        }

        _service.Paused += OnPaused;
        _service.Resumed += OnResumed;
        _service.Exited += OnExited;
        _service.OutputReceived += (_, text) => OutputReceived?.Invoke(this, text);
        _service.Faulted += (_, ex) => Failed?.Invoke(this, ex.Message);

        if (_service is InterpreterDebugService interpreted)
            interpreted.InputRequested += (_, request) => InputRequested?.Invoke(this, request);

        foreach (var (file, list) in _breakpoints)
            await _service.SetBreakpointsAsync(file, list, ct).ConfigureAwait(true);

        try
        {
            await _service.LaunchAsync(assemblyPath, workingDirectory, ct).ConfigureAwait(true);
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

    public Task ContinueAsync(CancellationToken ct = default) =>
        _service?.ContinueAsync(ct) ?? Task.CompletedTask;

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
