using System.Text.Json.Nodes;
using Basalt.Core.Services;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Debugging .NET programs through netcoredbg, spoken to over the Debug
/// Adapter Protocol.
///
/// The same adapter serves Visual Basic and C#: it works from the debug symbols
/// the compiler emits, so nothing here is language-specific.
/// </summary>
public sealed class NetCoreDebugService : IDebugSessionService
{
    private readonly string _adapterPath;
    private DapClient? _client;

    private int _currentThread;
    private readonly Dictionary<string, IReadOnlyList<Breakpoint>> _breakpoints = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolved path back to the spelling the IDE uses.
    ///
    /// The debugger reports frames with links resolved, which would not match
    /// the path an editor tab was opened under. Mapping back means "open where
    /// it stopped" finds the file the user already has in front of them.
    /// </summary>
    private readonly Dictionary<string, string> _pathsAsGiven = new(StringComparer.Ordinal);

    public NetCoreDebugService(string adapterPath) => _adapterPath = adapterPath;

    /// <summary>Whether a debugging session is under way.</summary>
    public bool IsRunning => _client is { IsRunning: true };

    /// <summary>Whether the program is stopped at a breakpoint or a step.</summary>
    public bool IsPaused { get; private set; }

    public event EventHandler<StackFrame>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler<int>? Exited;

    /// <summary>Text the debugged program wrote to its console.</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// Something went wrong on a background thread.
    ///
    /// Debugger events arrive detached from any request, so a failure while
    /// handling one has nowhere to be thrown. It is reported here instead.
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    public async Task LaunchAsync(
        string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
    {
        await StartAdapterAsync(ct).ConfigureAwait(false);

        _client!.Send("launch", new JsonObject
        {
            ["program"] = assemblyPath,
            ["cwd"] = workingDirectory ?? Path.GetDirectoryName(assemblyPath),
            ["stopAtEntry"] = false
        });

        await ReadyForBreakpointsAsync(ct).ConfigureAwait(false);
        await ResendBreakpointsAsync(ct).ConfigureAwait(false);

        _client.Send("configurationDone");
    }

    public async Task AttachAsync(int processId, CancellationToken ct = default)
    {
        await StartAdapterAsync(ct).ConfigureAwait(false);

        _client!.Send("attach", new JsonObject { ["processId"] = processId });

        await ReadyForBreakpointsAsync(ct).ConfigureAwait(false);
        await ResendBreakpointsAsync(ct).ConfigureAwait(false);

        _client.Send("configurationDone");
    }

    private async Task StartAdapterAsync(CancellationToken ct)
    {
        Stop();

        _hits.Clear();

        _client = new DapClient(_adapterPath);
        _client.EventReceived += OnEvent;
        _client.Exited += (_, code) =>
        {
            IsPaused = false;
            Exited?.Invoke(this, code);
        };

        // netcoredbg raises "initialized" while it is still answering
        // "initialize", so the watch is armed before the request goes out.
        // Waiting for it afterwards would wait for an event already past.
        _initialized = WaitForEventAsync("initialized", CancellationToken.None);

        await _client.SendAsync("initialize", new JsonObject
        {
            ["adapterID"] = "basalt",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["pathFormat"] = "path",
            ["supportsVariableType"] = true
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Set once the adapter will accept breakpoints.
    ///
    /// The protocol says breakpoints go after the "initialized" event and
    /// before "configurationDone"; sending them earlier leaves them unbound.
    /// </summary>
    private Task? _initialized;

    private async Task ReadyForBreakpointsAsync(CancellationToken ct)
    {
        if (_initialized is null) return;

        await _initialized.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
    }

    public async Task SetBreakpointsAsync(
        string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct = default)
    {
        _breakpoints[filePath] = breakpoints;

        // Breakpoints can be set before a session starts; they are sent to the
        // adapter once there is one.
        if (_client is null || !_client.IsRunning) return;

        await SendBreakpointsAsync(filePath, breakpoints, ct).ConfigureAwait(false);
    }

    private async Task ResendBreakpointsAsync(CancellationToken ct)
    {
        foreach (var (file, breakpoints) in _breakpoints)
            await SendBreakpointsAsync(file, breakpoints, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a path the way the debugger will see it.
    ///
    /// netcoredbg matches breakpoints against the paths recorded in the debug
    /// symbols by comparing text, so a path reached through a symlink never
    /// matches and the breakpoint is silently ignored. On macOS this is not an
    /// edge case: /tmp is a symlink to /private/tmp, and a project built there
    /// records the resolved form. Resolving here makes the two agree.
    /// </summary>
    private static string ResolvePath(string filePath)
    {
        try
        {
            var full = Path.GetFullPath(filePath);

            // ResolveLinkTarget returns null when the path is not a link, and
            // needs the file to exist; either way the original is the answer.
            return File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName
                   ?? ResolveThroughDirectories(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return filePath;
        }
    }

    /// <summary>
    /// Resolves links in the directories leading to a file.
    ///
    /// The file itself is usually real while a directory above it is the link,
    /// which is the case for /tmp, so the containing directory is what has to
    /// be resolved.
    /// </summary>
    private static string ResolveThroughDirectories(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null) return fullPath;

        var resolved = Directory.ResolveLinkTarget(directory, returnFinalTarget: true)?.FullName;

        if (resolved is not null)
            return Path.Combine(resolved, Path.GetFileName(fullPath));

        // Walk up: a link may be further along, as with /tmp/a/b/c.vb.
        var parent = Path.GetDirectoryName(directory);

        if (parent is null || parent == directory) return fullPath;

        var resolvedParent = ResolveThroughDirectories(directory);

        return resolvedParent == directory
            ? fullPath
            : Path.Combine(resolvedParent, Path.GetFileName(fullPath));
    }

    /// <summary>
    /// Prepares a condition for the debugger's evaluator.
    ///
    /// The evaluator only speaks C#, so a condition on a Visual Basic file is
    /// translated first. Without it "i = 4" is taken as an assignment and the
    /// breakpoint stops on every pass.
    /// </summary>
    private static string TranslateCondition(string condition, string filePath)
    {
        var extension = Path.GetExtension(filePath);

        var isBasic =
            extension.Equals(".vb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbhtml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bas", StringComparison.OrdinalIgnoreCase);

        return isBasic ? VbConditionTranslator.ToEvaluatorSyntax(condition) : condition;
    }

    private async Task SendBreakpointsAsync(
        string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct)
    {
        var lines = new JsonArray();

        foreach (var breakpoint in breakpoints.Where(b => b.Enabled))
        {
            var entry = new JsonObject { ["line"] = breakpoint.Line };

            if (breakpoint.Condition is { Length: > 0 } condition)
                entry["condition"] = TranslateCondition(condition, filePath);

            // "hitCondition" is deliberately not sent: netcoredbg ignores it in
            // every spelling. Verified by sending "3", ">= 3", "== 3" and ">2",
            // all of which stopped on the first pass. The passes are counted
            // here instead, in OnStopped.

            lines.Add(entry);
        }

        var resolved = ResolvePath(filePath);
        if (resolved != filePath) _pathsAsGiven[resolved] = filePath;

        await _client!.SendAsync("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = resolved },
            ["breakpoints"] = lines
        }, ct).ConfigureAwait(false);
    }

    public Task ContinueAsync(CancellationToken ct = default) =>
        ResumeAsync("continue", ct);

    public Task StepOverAsync(CancellationToken ct = default) =>
        ResumeAsync("next", ct);

    public Task StepIntoAsync(CancellationToken ct = default) =>
        ResumeAsync("stepIn", ct);

    public Task StepOutAsync(CancellationToken ct = default) =>
        ResumeAsync("stepOut", ct);

    private async Task ResumeAsync(string command, CancellationToken ct)
    {
        if (_client is null || !IsPaused) return;

        IsPaused = false;
        Resumed?.Invoke(this, EventArgs.Empty);

        await _client.SendAsync(command, new JsonObject
        {
            ["threadId"] = _currentThread
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StackFrame>> GetCallStackAsync(CancellationToken ct = default)
    {
        if (_client is null || !IsPaused) return [];

        var response = await _client.SendAsync("stackTrace", new JsonObject
        {
            ["threadId"] = _currentThread
        }, ct).ConfigureAwait(false);

        var frames = response["body"]?["stackFrames"] as JsonArray;
        if (frames is null) return [];

        return frames
            .OfType<JsonObject>()
            .Select(f => new StackFrame(
                f["name"]?.GetValue<string>() ?? "",
                AsGiven((f["source"] as JsonObject)?["path"]?.GetValue<string>()),
                f["line"]?.GetValue<int>() ?? 0))
            .ToList();
    }

    /// <summary>Reports a path in the spelling the IDE knows it by.</summary>
    private string? AsGiven(string? path)
    {
        if (path is null) return null;

        return _pathsAsGiven.TryGetValue(path, out var original) ? original : path;
    }

    /// <summary>
    /// Frame identifiers from the last call stack, needed to ask for locals.
    /// </summary>
    private readonly List<int> _frameIds = [];

    public async Task<IReadOnlyList<VariableValue>> GetLocalsAsync(
        int frameIndex, CancellationToken ct = default)
    {
        if (_client is null || !IsPaused) return [];

        await RefreshFrameIdsAsync(ct).ConfigureAwait(false);

        if (frameIndex < 0 || frameIndex >= _frameIds.Count) return [];

        var scopes = await _client.SendAsync("scopes", new JsonObject
        {
            ["frameId"] = _frameIds[frameIndex]
        }, ct).ConfigureAwait(false);

        var scopeList = scopes["body"]?["scopes"] as JsonArray;
        if (scopeList is null) return [];

        var values = new List<VariableValue>();

        foreach (var scope in scopeList.OfType<JsonObject>())
        {
            var reference = scope["variablesReference"]?.GetValue<int>() ?? 0;
            if (reference == 0) continue;

            values.AddRange(await ReadVariablesAsync(reference, ct).ConfigureAwait(false));
        }

        return values;
    }

    /// <summary>Expands a variable that has children, such as an object.</summary>
    public async Task<IReadOnlyList<VariableValue>> ExpandAsync(
        int variablesReference, CancellationToken ct = default)
    {
        if (_client is null || !IsPaused || variablesReference == 0) return [];

        return await ReadVariablesAsync(variablesReference, ct).ConfigureAwait(false);
    }

    private async Task<List<VariableValue>> ReadVariablesAsync(int reference, CancellationToken ct)
    {
        var response = await _client!.SendAsync("variables", new JsonObject
        {
            ["variablesReference"] = reference
        }, ct).ConfigureAwait(false);

        var variables = response["body"]?["variables"] as JsonArray;
        if (variables is null) return [];

        return variables
            .OfType<JsonObject>()
            .Select(v => new VariableValue(
                v["name"]?.GetValue<string>() ?? "",
                v["value"]?.GetValue<string>() ?? "",
                v["type"]?.GetValue<string>() ?? "",
                (v["variablesReference"]?.GetValue<int>() ?? 0) != 0))
            .ToList();
    }

    private async Task RefreshFrameIdsAsync(CancellationToken ct)
    {
        var response = await _client!.SendAsync("stackTrace", new JsonObject
        {
            ["threadId"] = _currentThread
        }, ct).ConfigureAwait(false);

        _frameIds.Clear();

        if (response["body"]?["stackFrames"] is not JsonArray frames) return;

        _frameIds.AddRange(frames
            .OfType<JsonObject>()
            .Select(f => f["id"]?.GetValue<int>() ?? 0));
    }

    public async Task<string?> EvaluateAsync(
        string expression, int frameIndex, CancellationToken ct = default)
    {
        if (_client is null || !IsPaused) return null;

        await RefreshFrameIdsAsync(ct).ConfigureAwait(false);

        var arguments = new JsonObject
        {
            ["expression"] = expression,
            ["context"] = "watch"
        };

        if (frameIndex >= 0 && frameIndex < _frameIds.Count)
            arguments["frameId"] = _frameIds[frameIndex];

        try
        {
            var response = await _client.SendAsync("evaluate", arguments, ct).ConfigureAwait(false);

            return response["success"]?.GetValue<bool>() == true
                ? response["body"]?["result"]?.GetValue<string>()
                : null;
        }
        catch (IOException)
        {
            // The session ended while the expression was being evaluated.
            return null;
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Stop();
        return Task.CompletedTask;
    }

    private void Stop()
    {
        if (_client is null) return;

        try
        {
            _client.Send("disconnect", new JsonObject { ["terminateDebuggee"] = true });
        }
        catch (IOException)
        {
            // Already gone.
        }

        _client.Dispose();
        _client = null;
        IsPaused = false;
    }

    private void OnEvent(object? sender, DapEvent dapEvent)
    {
        switch (dapEvent.Name)
        {
            case "stopped":
                _currentThread = dapEvent.Body?["threadId"]?.GetValue<int>() ?? 0;
                IsPaused = true;
                _ = OnStoppedAsync();
                break;

            case "continued":
                IsPaused = false;
                Resumed?.Invoke(this, EventArgs.Empty);
                break;

            case "exited":
                IsPaused = false;
                Exited?.Invoke(this, dapEvent.Body?["exitCode"]?.GetValue<int>() ?? 0);
                break;

            case "output":
                if (dapEvent.Body?["output"]?.GetValue<string>() is { } text)
                    OutputReceived?.Invoke(this, text);
                break;
        }
    }

    /// <summary>Passes counted so far, by file and line.</summary>
    private readonly Dictionary<(string File, int Line), int> _hits = [];

    /// <summary>
    /// Decides whether a stop is one the user should see.
    ///
    /// A breakpoint asked to wait some passes stops every time as far as the
    /// debugger is concerned — it has no hit count of its own — so the passes
    /// are counted here and the early ones resumed without telling anyone.
    /// </summary>
    private async Task OnStoppedAsync()
    {
        try
        {
            var frames = await GetCallStackAsync().ConfigureAwait(false);

            var frame = frames.Count > 0 ? frames[0] : new StackFrame("", null, 0);

            if (ShouldSkip(frame))
            {
                // Sent directly rather than through ContinueAsync, which
                // refuses to act unless IsPaused is still set — and clearing
                // it first is what makes it refuse. No Resumed either: the
                // panels were never told this stop happened.
                IsPaused = false;

                if (_client is not null)
                {
                    await _client.SendAsync("continue", new JsonObject
                    {
                        ["threadId"] = _currentThread
                    }).ConfigureAwait(false);
                }

                return;
            }

            Paused?.Invoke(this, frame);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // The session ended between stopping and asking about it.
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, ex);
        }
    }

    /// <summary>Whether this stop is an early pass of a counted breakpoint.</summary>
    private bool ShouldSkip(StackFrame frame)
    {
        if (frame.FilePath is not { Length: > 0 } path) return false;

        var file = _pathsAsGiven.TryGetValue(path, out var original) ? original : path;

        if (!_breakpoints.TryGetValue(file, out var breakpoints)) return false;

        var breakpoint = breakpoints.FirstOrDefault(b => b.Line == frame.Line);

        if (breakpoint?.HitCount is not { } wanted || wanted <= 1) return false;

        var key = (file, frame.Line);

        _hits[key] = _hits.TryGetValue(key, out var seen) ? seen + 1 : 1;

        return _hits[key] < wanted;
    }

    /// <summary>Waits for a named event, used to follow the protocol's order.</summary>
    private Task WaitForEventAsync(string name, CancellationToken ct)
    {
        if (_client is null) return Task.CompletedTask;

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, DapEvent dapEvent)
        {
            if (dapEvent.Name != name) return;

            _client.EventReceived -= Handler;
            waiter.TrySetResult();
        }

        _client.EventReceived += Handler;
        ct.Register(() => waiter.TrySetCanceled(ct));

        return waiter.Task;
    }

    public void Dispose() => Stop();
}
