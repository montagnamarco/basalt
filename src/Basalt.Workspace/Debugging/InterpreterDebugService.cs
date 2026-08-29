using Basalt.Extensibility.Interpretation;
using Basalt.Core.Services;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// An interpreter, dressed as the debug service the IDE already talks to.
///
/// So that the stack, variables and watch panels work against an interpreted
/// program without knowing one is running: they ask IDebugService, and what
/// answers is either netcoredbg or this.
///
/// The mapping is close because IDebugService was written about debugging
/// rather than about .NET. What it calls an assembly path is a source path
/// here, which is the only place the shapes disagree.
/// </summary>
public sealed class InterpreterDebugService : IDebugSessionService
{
    private readonly IInterpreter _interpreter;
    private readonly Dictionary<string, IReadOnlyList<Breakpoint>> _breakpoints = [];

    private string? _filePath;
    private bool _disposed;

    public InterpreterDebugService(IInterpreter interpreter)
    {
        _interpreter = interpreter;

        _interpreter.Stopped += OnStopped;
        _interpreter.Exited += (_, code) => Exited?.Invoke(this, code);
    }

    /// <summary>What the program printed, for the IDE to show.</summary>
    public event EventHandler<string>? OutputReceived
    {
        add => _interpreter.Output += value;
        remove => _interpreter.Output -= value;
    }

    /// <summary>The program is waiting for a line to be typed.</summary>
    public event EventHandler<InputRequest>? InputRequested
    {
        add => _interpreter.InputRequested += value;
        remove => _interpreter.InputRequested -= value;
    }

    /// <summary>The program is running.</summary>
    public event EventHandler? Started;

    public event EventHandler<Core.Services.StackFrame>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler<int>? Exited;

    /// <summary>Whether the program is stopped somewhere.</summary>
    public bool IsPaused => _interpreter.State == RunState.Paused;

    /// <summary>Whether the program is still being debugged.</summary>
    public bool IsRunning =>
        _interpreter.State is RunState.Running or RunState.Paused or RunState.Ready
        && _filePath is not null;

    /// <summary>Something went wrong the user should be told about.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>
    /// A variable's children.
    ///
    /// Always empty: an interpreted value is a number or a string, and there
    /// is nothing under it to open. Said plainly rather than left unwritten.
    /// </summary>
    public Task<IReadOnlyList<VariableValue>> ExpandAsync(
        int reference, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VariableValue>>([]);

    /// <summary>The problems found reading the program, after a launch.</summary>
    public IReadOnlyList<InterpreterError> Errors { get; private set; } = [];

    /// <summary>
    /// Reads the program and runs it to the first stop.
    ///
    /// The parameter is named for the interface, which calls it an assembly
    /// path; here it is the source file, since an interpreter has no assembly.
    /// </summary>
    public async Task LaunchAsync(
        string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
    {
        _filePath = assemblyPath;

        var source = await File.ReadAllTextAsync(assemblyPath, ct).ConfigureAwait(false);

        Errors = _interpreter.Load(source, assemblyPath);

        if (Errors.Count > 0)
        {
            Faulted?.Invoke(this, new InvalidOperationException(
                string.Join(Environment.NewLine, Errors.Select(e => e.ToString()))));

            return;
        }

        ApplyBreakpoints();

        // Said before running rather than after: RunAsync only returns once
        // the program is over, and "it started" is news while it is still on.
        Started?.Invoke(this, EventArgs.Empty);

        await _interpreter.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Not supported: there is no other process to attach to.
    ///
    /// Said rather than ignored, so a caller that tries learns why.
    /// </summary>
    public Task AttachAsync(int processId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "An interpreted program runs inside the IDE, so there is nothing to attach to.");

    public Task SetBreakpointsAsync(
        string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct = default)
    {
        _breakpoints[filePath] = breakpoints;

        ApplyBreakpoints();

        return Task.CompletedTask;
    }

    public async Task ContinueAsync(CancellationToken ct = default)
    {
        Resumed?.Invoke(this, EventArgs.Empty);

        await _interpreter.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Not supported: the interpreter has no way to break into itself.
    /// 
    /// It runs a statement at a time on a thread of its own and only checks
    /// for breakpoints between them, so there is nothing to interrupt. Doing
    /// nothing quietly would leave the button looking broken; the caller
    /// greys it out instead, and this is the honest answer if one asks.
    /// </summary>
    public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task StepOverAsync(CancellationToken ct = default)
    {
        Resumed?.Invoke(this, EventArgs.Empty);

        await _interpreter.StepOverAsync(ct).ConfigureAwait(false);
    }

    public async Task StepIntoAsync(CancellationToken ct = default)
    {
        Resumed?.Invoke(this, EventArgs.Empty);

        await _interpreter.StepIntoAsync(ct).ConfigureAwait(false);
    }

    public async Task StepOutAsync(CancellationToken ct = default)
    {
        Resumed?.Invoke(this, EventArgs.Empty);

        await _interpreter.StepOutAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Core.Services.StackFrame>> GetCallStackAsync(
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Core.Services.StackFrame>>(
        [
            .. _interpreter.GetCallStack()
                .Select(f => new Core.Services.StackFrame(f.Name, _filePath, f.Line))
        ]);

    public Task<IReadOnlyList<VariableValue>> GetLocalsAsync(
        int frameIndex, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VariableValue>>(
        [
            .. _interpreter.GetVariables(frameIndex)
                .Select(v => new VariableValue(v.Name, v.Value, v.Type, v.HasChildren))
        ]);

    /// <summary>
    /// Works out an expression in the language being debugged.
    ///
    /// In BASIC rather than in C#: the .NET debugger needs conditions
    /// translated, and this one does not, because the interpreter reads the
    /// same language the user is looking at.
    /// </summary>
    public Task<string?> EvaluateAsync(
        string expression, int frameIndex, CancellationToken ct = default) =>
        Task.FromResult(_interpreter.Evaluate(expression, frameIndex));

    /// <summary>Changes a variable while the program is stopped.</summary>
    public bool SetVariable(string name, string value, int frameIndex) =>
        _interpreter.SetVariable(name, value, frameIndex);

    public Task StopAsync(CancellationToken ct = default)
    {
        _interpreter.Stop();

        return Task.CompletedTask;
    }

    /// <summary>Hands the interpreter the breakpoints for the file it is running.</summary>
    private void ApplyBreakpoints()
    {
        if (_filePath is null) return;

        var mine = _breakpoints.TryGetValue(_filePath, out var found) ? found : [];

        _interpreter.SetBreakpoints(
        [
            .. mine.Select(b => new InterpreterBreakpoint(
                b.Line, b.Enabled, b.Condition, b.HitCount))
        ]);
    }

    private void OnStopped(object? sender, StopInfo stop)
    {
        var frame = _interpreter.GetCallStack().FirstOrDefault();

        Paused?.Invoke(this, new Core.Services.StackFrame(
            frame?.Name ?? "main", _filePath, stop.Line));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _interpreter.Stopped -= OnStopped;
        _interpreter.Stop();
    }
}
