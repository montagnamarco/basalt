namespace Basalt.Core.Services;

/// <summary>
/// A place to stop.
///
/// <paramref name="Condition"/> is an expression in the language of the file;
/// <paramref name="HitCount"/> stops only from the nth hit onwards. Both are
/// evaluated by the debugger, not here.
/// </summary>
public sealed record Breakpoint(
    string FilePath,
    int Line,
    bool Enabled = true,
    string? Condition = null,
    int? HitCount = null);

public sealed record StackFrame(string Method, string? FilePath, int Line);
public sealed record VariableValue(string Name, string Value, string Type, bool HasChildren);

/// <summary>
/// Managed debugging. The intended implementation uses netcoredbg through the
/// Debug Adapter Protocol, so that it stays identical on all three platforms.
/// </summary>
public interface IDebugService
{
    Task LaunchAsync(string assemblyPath, string? workingDirectory = null, CancellationToken ct = default);
    Task AttachAsync(int processId, CancellationToken ct = default);
    Task SetBreakpointsAsync(string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct = default);
    Task ContinueAsync(CancellationToken ct = default);
    Task StepOverAsync(CancellationToken ct = default);
    Task StepIntoAsync(CancellationToken ct = default);
    Task StepOutAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StackFrame>> GetCallStackAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VariableValue>> GetLocalsAsync(int frameIndex, CancellationToken ct = default);
    Task<string?> EvaluateAsync(string expression, int frameIndex, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    event EventHandler<StackFrame>? Paused;
    event EventHandler? Resumed;
    event EventHandler<int>? Exited;
}
