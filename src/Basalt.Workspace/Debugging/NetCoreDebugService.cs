using System.Text.Json.Nodes;
using Basalt.Core.Localization;
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
    private readonly Func<IDapConnection> _connect;
    private IDapConnection? _client;

    private int _currentThread;

    /// <summary>Why the program last stopped, as the adapter said: "breakpoint", "step", "exception", "pause".</summary>
    private volatile string? _stopReason;

    /// <inheritdoc />
    public string? StoppedException { get; private set; }
    private readonly Dictionary<string, IReadOnlyList<Breakpoint>> _breakpoints = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolved path back to the spelling the IDE uses.
    ///
    /// The debugger reports frames with links resolved, which would not match
    /// the path an editor tab was opened under. Mapping back means "open where
    /// it stopped" finds the file the user already has in front of them.
    /// </summary>
    private readonly Dictionary<string, string> _pathsAsGiven = new(StringComparer.Ordinal);

    /// <summary>
    /// The start of a resolved path and the start the IDE spells it with, learnt from the program launched:
    /// "/private/var/" and "/var/" when the project sits under a linked directory. A file with no breakpoint
    /// in it, reached by stepping or by an exception, is reported resolved and has no entry above.
    /// </summary>
    private (string Resolved, string Given)? _prefixAsGiven;

    public NetCoreDebugService(string adapterPath)
        : this(() => new DapClient(adapterPath))
    {
    }

    /// <summary>Connects through something other than the adapter process, for the tests.</summary>
    internal NetCoreDebugService(Func<IDapConnection> connect) => _connect = connect;

    /// <summary>Whether a debugging session is under way.</summary>
    public bool IsRunning => _client is { IsRunning: true } && Volatile.Read(ref _debuggeeExited) == 0;

    /// <summary>
    /// Whether the program being debugged has ended.
    /// </summary>
    /// <remarks>
    /// netcoredbg outlives the program: after "exited" the adapter process is
    /// still there, so a session judged by the adapter alone stayed "running"
    /// for good, the banner said so, and F5 did nothing until Stop. Written on
    /// the adapter's reading thread, read on the interface thread. An int, so
    /// the "exited" event and the adapter's own exit, which can race on a
    /// Stop, report the ending only once (MarkExited).
    /// </remarks>
    private int _debuggeeExited;

    /// <summary>Records that the program ended; true only for the first caller.</summary>
    private bool MarkExited() => Interlocked.Exchange(ref _debuggeeExited, 1) == 0;

    /// <summary>Whether the program is stopped at a breakpoint or a step.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Counts every stop and resume, so that a request prepared in one stop
    /// can tell the program has moved on before it is sent.
    /// </summary>
    private int _stopGeneration;

    /// <summary>
    /// The stop generation that was last reported to the interface.
    ///
    /// <see cref="IsPaused"/> is set the moment "stopped" arrives, before a
    /// breakpoint that waits for a count has decided to resume silently. A
    /// stop the user was never shown is not one to evaluate in: this equals
    /// the current generation only for a stop that was shown and has not
    /// been left since.
    /// </summary>
    private int _shownGeneration = -1;

    private bool IsStopShown =>
        Volatile.Read(ref _shownGeneration) == Volatile.Read(ref _stopGeneration);

    public event EventHandler<StackFrame>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler<int>? Exited;

    /// <summary>Text the debugged program wrote to its console.</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// What the program wrote through Debug and Trace (Debug.Print,
    /// Debug.WriteLine), which Visual Basic 6 sent to the Immediate window.
    ///
    /// netcoredbg reports it as an "output" event of category "console",
    /// apart from the program's own stdout and stderr. It is also raised
    /// through <see cref="OutputReceived"/>, so nothing that listened there
    /// before loses it.
    /// </summary>
    public event EventHandler<string>? DebugOutputReceived;

    /// <summary>The program is running, whether or not it will ever stop.</summary>
    public event EventHandler? Started;

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
        _prefixAsGiven = LinkedPrefix(Path.GetFullPath(assemblyPath));

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

        Volatile.Write(ref _debuggeeExited, 0);
        _client = _connect();
        _client.EventReceived += OnEvent;
        _client.Exited += (_, code) =>
        {
            Interlocked.Increment(ref _stopGeneration);
            IsPaused = false;

            // The program's own "exited" already said it ended; the adapter
            // closing afterwards is not a second ending to report.
            if (MarkExited()) Exited?.Invoke(this, code);
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
    ///
    /// Internal rather than private so a test that builds its own PDB in
    /// memory (rather than through "dotnet build", whose working directory
    /// the kernel already canonicalizes) can give the compiler the same
    /// resolved path it will later ask the debugger to break on.
    /// </summary>
    internal static string ResolvePath(string filePath)
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
    /// The first linked directory on the way to a path, as the IDE spells it and as it resolves:
    /// ("/private/var/", "/var/") for a project under /var on macOS. Null when no directory on the way is a link.
    /// </summary>
    /// <remarks>
    /// Found by asking each directory from the root down whether it is a link, not by comparing the two
    /// spellings: "/var" against "/private/var" have "var" in common, and a comparison of the ends would map
    /// "/private/" to "/" and rewrite paths that never went through the link.
    /// </remarks>
    internal static (string Resolved, string Given)? LinkedPrefix(string givenPath)
    {
        try
        {
            var full = Path.GetFullPath(givenPath);
            var root = Path.GetPathRoot(full) ?? "";
            var directory = root;

            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).SkipLast(1))
            {
                directory = Path.Combine(directory, part);

                if (new DirectoryInfo(directory).LinkTarget is null) continue;

                var target = Directory.ResolveLinkTarget(directory, returnFinalTarget: true)?.FullName;

                return target is null ? null : (WithSeparator(target), WithSeparator(directory));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Unreadable on the way: paths are reported as the adapter gives them, as before.
        }

        return null;
    }

    private static string WithSeparator(string directory) =>
        directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;

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
        return IsBasicSource(filePath) ? VbConditionTranslator.ToEvaluatorSyntax(condition) : condition;
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

    /// <summary>
    /// Breaks into a running program.
    ///
    /// Not routed through ResumeAsync, which refuses unless the program is
    /// stopped: this is the one command that means anything only while it
    /// is running. The "stopped" event that follows does the rest.
    /// </summary>
    public async Task PauseAsync(CancellationToken ct = default)
    {
        if (_client is null || IsPaused) return;

        // The adapter names a thread on its own only once something has
        // stopped, and the "thread" event that would name one can arrive
        // after the program is already reported running. Asked with no
        // thread, netcoredbg refuses the request outright (0x80070057), so
        // the threads are fetched when none is known yet.
        if (_currentThread == 0) await AdoptAThreadAsync(ct).ConfigureAwait(false);

        if (_currentThread == 0) return;

        await _client.SendAsync("pause", new JsonObject
        {
            ["threadId"] = _currentThread
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Takes the first thread the adapter admits to having.</summary>
    private async Task AdoptAThreadAsync(CancellationToken ct)
    {
        if (_client is null) return;

        var answer = await _client
            .SendAsync("threads", new JsonObject(), ct)
            .ConfigureAwait(false);

        if (answer?["body"]?["threads"] is not JsonArray threads) return;

        foreach (var thread in threads)
        {
            if (thread?["id"]?.GetValue<int>() is { } id and not 0)
            {
                _currentThread = id;
                return;
            }
        }
    }

    public Task StepOverAsync(CancellationToken ct = default) =>
        ResumeAsync("next", ct);

    public Task StepIntoAsync(CancellationToken ct = default) =>
        ResumeAsync("stepIn", ct);

    public Task StepOutAsync(CancellationToken ct = default) =>
        ResumeAsync("stepOut", ct);

    private async Task ResumeAsync(string command, CancellationToken ct)
    {
        if (_client is null || !IsPaused) return;

        Interlocked.Increment(ref _stopGeneration);
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
            .Select(f =>
            {
                var name = f["name"]?.GetValue<string>() ?? "";

                // A Visual Basic frame is named as its source names it: "ComputeAsync()", not its state
                // machine's MoveNext; "Fail(n As Integer)", not "Fail(Int32 n)".
                return new StackFrame(
                    IsBasicSource(SourcePathOf(f)) ? VbDisplay.FrameName(name) : name,
                    AsGiven((f["source"] as JsonObject)?["path"]?.GetValue<string>()),
                    f["line"]?.GetValue<int>() ?? 0);
            })
            .ToList();
    }

    /// <summary>Reports a path in the spelling the IDE knows it by.</summary>
    private string? AsGiven(string? path)
    {
        if (path is null) return null;

        if (_pathsAsGiven.TryGetValue(path, out var original)) return original;

        return _prefixAsGiven is var (resolved, given) && path.StartsWith(resolved, StringComparison.Ordinal)
            ? given + path[resolved.Length..]
            : path;
    }

    /// <summary>
    /// A frame of the stopped thread: the id to ask the adapter about it by, null when that id cannot be known,
    /// and its source file or "".
    /// </summary>
    private readonly record struct FrameRef(int? Id, string SourcePath);

    public async Task<IReadOnlyList<VariableValue>> GetLocalsAsync(
        int frameIndex, CancellationToken ct = default)
    {
        var client = _client;

        if (client is null || !IsPaused) return [];

        var frames = await ReadFramesAsync(client, ct).ConfigureAwait(false);

        if (frameIndex < 0 || frameIndex >= frames.Count || frames[frameIndex].Id is not { } frameId) return [];

        var locals = await ReadLocalsAsync(client, frameId, ct).ConfigureAwait(false);
        var basic = IsBasicSource(frames[frameIndex].SourcePath);

        // Children opened later in the Variables panel belong to the frame read last.
        _expandAsBasic = basic;

        return basic ? [.. locals.Select(AsBasic)] : locals;
    }

    /// <summary>Whether the variables expanded next are written as Visual Basic: those of the frame read last.</summary>
    private volatile bool _expandAsBasic;

    /// <summary>A variable written as Visual Basic: Me, Integer, Nothing.</summary>
    private static VariableValue AsBasic(VariableValue value) => value with
    {
        Name = VbDisplay.Name(value.Name),
        Type = VbDisplay.Type(value.Type),
        Value = VbDisplay.Value(value.Value),
    };

    /// <summary>A frame's locals as the user wrote them, an Async or Iterator method's included.</summary>
    private static async Task<IReadOnlyList<VariableValue>> ReadLocalsAsync(IDapConnection client, int frameId, CancellationToken ct)
    {

        var scopes = await client.SendAsync("scopes", new JsonObject
        {
            ["frameId"] = frameId
        }, ct).ConfigureAwait(false);

        var scopeList = scopes["body"]?["scopes"] as JsonArray;
        if (scopeList is null) return [];

        var values = new List<VariableValue>();

        foreach (var scope in scopeList.OfType<JsonObject>())
        {
            var reference = scope["variablesReference"]?.GetValue<int>() ?? 0;
            if (reference == 0) continue;

            values.AddRange(await ReadVariablesAsync(client, reference, ct).ConfigureAwait(false));
        }

        return await UnhoistAsync(client, values, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The locals of an Async or Iterator method, which the compiler moves into fields of a generated state
    /// machine. Measured on netcoredbg 3.2.0: in ComputeAsync(n) the frame holds only "this", typed
    /// Program.VB$StateMachine_1_ComputeAsync, and n, before and doubled are its fields $VB$Local_n,
    /// $VB$ResumableLocal_before$0 and $VB$ResumableLocal_doubled$1. They are shown under their own names, with
    /// the method's Me ($VB$Me) as Me; the machine's own fields ($State, $Builder, awaiters) are left out.
    /// </summary>
    private static async Task<IReadOnlyList<VariableValue>> UnhoistAsync(
        IDapConnection client, IReadOnlyList<VariableValue> values, CancellationToken ct)
    {
        var holder = values.FirstOrDefault(IsGeneratedHolder);

        if (holder is not { Reference: > 0 }) return values;

        var hoisted = await HoistedLocalsAsync(client, holder.Reference, depth: 0, ct).ConfigureAwait(false);
        var unhoisted = new List<VariableValue>();

        foreach (var value in values)
        {
            if (ReferenceEquals(value, holder)) unhoisted.AddRange(hoisted);
            else unhoisted.Add(value);
        }

        return unhoisted;
    }

    /// <summary>
    /// A frame's "this" that is not the user's object but one the compiler made: an Async or Iterator method's
    /// state machine, or a lambda's closure, whose fields are the locals the source declared or captured.
    /// </summary>
    private static bool IsGeneratedHolder(VariableValue value) =>
        value.Name == "this"
        && (value.Type.Contains("VB$StateMachine_", StringComparison.Ordinal)
            || value.Type.Contains("_Closure$__", StringComparison.Ordinal));

    /// <summary>
    /// The fields of a state machine or a closure that hold source locals, under their source names. A closure
    /// the machine holds (locals captured by a lambda inside an Async method) is opened in turn. Two fields may
    /// carry one name (two loops each declaring "item" around an Await): both are shown, since which one is in
    /// scope where the program stopped is not in what the adapter reports, and a guess would show a value
    /// from the other loop as if it were this one.
    /// </summary>
    private static async Task<List<VariableValue>> HoistedLocalsAsync(
        IDapConnection client, int reference, int depth, CancellationToken ct)
    {
        var fields = await ReadVariablesAsync(client, reference, ct).ConfigureAwait(false);
        var hoisted = new List<VariableValue>();

        foreach (var field in fields)
        {
            if (depth < 3 && field.Reference > 0 && IsClosureField(field))
            {
                hoisted.AddRange(await HoistedLocalsAsync(client, field.Reference, depth + 1, ct).ConfigureAwait(false));
                continue;
            }

            if (SourceNameOfHoisted(field.Name) is { } name) hoisted.Add(field with { Name = name });
        }

        return hoisted;
    }

    /// <summary>A field holding a closure: its type is one, or its name says so ("$VB$ResumableLocal_$VB$Closure_2$1").</summary>
    private static bool IsClosureField(VariableValue field) =>
        field.Type.Contains("_Closure$__", StringComparison.Ordinal)
        || field.Name.Contains("$VB$Closure_", StringComparison.Ordinal);

    /// <summary>The name a hoisted field had in the source, or null for the state machine's own fields.</summary>
    internal static string? SourceNameOfHoisted(string fieldName) => SourceNameOfHoisted(fieldName, out _);

    /// <param name="slot">The compiler's slot number for a resumable local, -1 for any other field.</param>
    internal static string? SourceNameOfHoisted(string fieldName, out int slot)
    {
        slot = -1;

        if (fieldName == "$VB$Me") return "Me";

        const string local = "$VB$Local_";
        const string resumable = "$VB$ResumableLocal_";

        string? name = null;

        if (fieldName.StartsWith(local, StringComparison.Ordinal))
        {
            name = fieldName[local.Length..];
        }
        else if (fieldName.StartsWith(resumable, StringComparison.Ordinal))
        {
            var rest = fieldName[resumable.Length..];
            var dollar = rest.LastIndexOf('$');

            if (dollar > 0 && int.TryParse(rest[(dollar + 1)..], out var number)) slot = number;

            name = dollar > 0 ? rest[..dollar] : rest;
        }

        // A VB identifier cannot hold "$": what is left is the compiler's own (a closure, a spill), not a local.
        return name is { Length: > 0 } && !name.Contains('$') ? name : null;
    }

    /// <summary>Expands a variable that has children, such as an object.</summary>
    public async Task<IReadOnlyList<VariableValue>> ExpandAsync(
        int variablesReference, CancellationToken ct = default)
    {
        var client = _client;

        if (client is null || !IsPaused || variablesReference == 0) return [];

        var children = await ReadVariablesAsync(client, variablesReference, ct).ConfigureAwait(false);

        return _expandAsBasic ? [.. children.Select(AsBasic)] : children;
    }

    /// <summary>The children of a reference, asked on the connection the caller read once: Stop clears the field.</summary>
    private static async Task<List<VariableValue>> ReadVariablesAsync(IDapConnection client, int reference, CancellationToken ct)
    {
        var response = await client.SendAsync("variables", new JsonObject
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
                (v["variablesReference"]?.GetValue<int>() ?? 0) != 0)
            {
                Reference = v["variablesReference"]?.GetValue<int>() ?? 0
            })
            .ToList();
    }

    /// <summary>
    /// The stopped thread's frames, read fresh for each caller. Each request keeps its own list: a watch, a
    /// hover and the Variables panel ask at once, on pool threads, and a list shared between them was read
    /// half rewritten.
    /// </summary>
    private async Task<IReadOnlyList<FrameRef>> ReadFramesAsync(IDapConnection client, CancellationToken ct)
    {
        // Read before the stack: a shift measured on these frames must be filed under the stop they belong to.
        var generation = Volatile.Read(ref _stopGeneration);

        var response = await client.SendAsync("stackTrace", new JsonObject
        {
            ["threadId"] = _currentThread
        }, ct).ConfigureAwait(false);

        if (response["body"]?["stackFrames"] is not JsonArray frames) return [];

        var shift = await FrameIdShiftAsync(client, frames, generation, ct).ConfigureAwait(false);

        return [.. frames.OfType<JsonObject>().Select(f => new FrameRef((f["id"]?.GetValue<int>() ?? 0) + shift, SourcePathOf(f)))];

    }

    /// <summary>The shift measured at one stop. A whole object, swapped at once: callers on several threads read it.</summary>
    private sealed record MeasuredShift(int Generation, int? Shift);

    private volatile MeasuredShift? _measuredShift;

    /// <summary>
    /// What to add to a frame's id to reach that frame's locals and evaluation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stopped on an unhandled exception, netcoredbg 3.2.0 lists only the program's own frames, without a module
    /// or column, but numbers its ids from the throw site, through the library frames it hides: the listed
    /// frame k answers to id k plus the number of hidden frames. Measured: a throw in user code three calls deep
    /// gave Fail, Middle and Main at ids 1, 2 and 3; a FormatException from Integer.Parse gave Middle and Main
    /// at 2 and 3, the hidden ids holding only $exception. At any other stop the ids are right, and the frames
    /// carry a module, which is how the two are told apart.
    /// </para>
    /// <para>
    /// The number is found once per stop, from the bottom: the last id the adapter answers for is Main's, so
    /// the ids past the listed frames are the hidden ones. Measured on both cases above: the last ids were 3,
    /// for three and for two listed frames. Hidden frames between listed ones (a lambda run by a library
    /// method) cannot be placed this way; the frames below them stay right.
    /// </para>
    /// </remarks>
    /// <returns>The shift, or null when it cannot be measured.</returns>
    private async Task<int?> FrameIdShiftAsync(IDapConnection client, JsonArray frames, int generation, CancellationToken ct)
    {
        if (_stopReason != "exception"
            || frames.FirstOrDefault() is not JsonObject top
            || top["moduleId"]?.GetValue<string>() is not (null or ""))
        {
            return 0;
        }

        if (_measuredShift is { } measured && measured.Generation == generation) return measured.Shift;

        var listed = frames.OfType<JsonObject>().ToList();
        var firstId = listed[0]["id"]?.GetValue<int>() ?? 0;

        // The last id the adapter knows is the outermost frame, Main; the hidden frames are the ones above the
        // listed, at the throw site, so their number is what the ids run past the listed frames. Asked id by id
        // from the last listed one, until the adapter knows no more.
        var lastKnown = -1;
        var endFound = false;

        // Up to the limit of hidden frames, and one further to see where the ids end.
        for (var id = firstId + listed.Count - 1; id <= firstId + listed.Count + HiddenFramesLimit; id++)
        {
            if (!await KnowsFrameAsync(client, id, ct).ConfigureAwait(false))
            {
                endFound = true;
                break;
            }

            lastKnown = id;
        }

        // Nothing known where Main should be, or no end within reach (more hidden frames than are looked through):
        // no locals are shown rather than another frame's.
        int? shift = lastKnown < 0 || !endFound ? null : lastKnown - (firstId + listed.Count - 1);

        _measuredShift = new MeasuredShift(generation, shift);

        return shift;
    }

    /// <summary>How many hidden library frames above a listed one are looked through.</summary>
    private const int HiddenFramesLimit = 16;

    /// <summary>Whether the adapter has a frame with this id: it refuses "scopes" for one it does not.</summary>
    private static async Task<bool> KnowsFrameAsync(IDapConnection client, int frameId, CancellationToken ct)
    {
        var scopes = await client.SendAsync("scopes", new JsonObject { ["frameId"] = frameId }, ct).ConfigureAwait(false);

        return scopes["success"]?.GetValue<bool>() == true;
    }

    private static string SourcePathOf(JsonObject? frame) =>
        (frame?["source"] as JsonObject)?["path"]?.GetValue<string>() ?? "";

    /// <summary>Whether a frame's source is Visual Basic, whose expressions the evaluator must have translated.</summary>
    private static bool IsBasicSource(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return extension.Equals(".vb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbhtml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbrazor", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bas", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What to send the evaluator for an expression typed in a frame: the C# readings of Visual Basic, likeliest
    /// first, or the text as it is for any other language.
    /// </summary>
    private static IReadOnlyList<string> EvaluatorForms(string expression, string sourcePath)
    {
        if (!IsBasicSource(sourcePath)) return [expression];

        var candidates = VbExpressionTranslator.Candidates(expression);

        // Text the translator could not read is still Visual Basic: sent as it is, "n = x" would be a C#
        // assignment in the Immediate window. The comparison translation turns its "=" into "==" first.
        return candidates.Count == 0 || (candidates.Count == 1 && candidates[0] == expression.Trim())
            ? [VbConditionTranslator.ToEvaluatorSyntax(expression.Trim())]
            : candidates;
    }

    public async Task<string?> EvaluateAsync(
        string expression, int frameIndex, CancellationToken ct = default)
    {
        // Read once: several readings are sent in turn, and Stop clears the field between any two of them.
        var client = _client;

        if (client is null || !IsPaused) return null;

        var generation = Volatile.Read(ref _stopGeneration);

        try
        {
            var frames = await ReadFramesAsync(client, ct).ConfigureAwait(false);
            int? frameId = frameIndex >= 0 && frameIndex < frames.Count ? frames[frameIndex].Id : null;
            var sourcePath = frameIndex >= 0 && frameIndex < frames.Count ? frames[frameIndex].SourcePath : "";

            // A frame whose id cannot be known: without one the adapter would answer for the top frame instead.
            if (frameIndex >= 0 && frameIndex < frames.Count && frameId is null) return null;

            // A plain name is looked up among the frame's locals first: an Async or Iterator method keeps its
            // locals in fields the evaluator cannot name ($VB$Local_n), and "Me" there would be the state machine.
            if (frameId is { } localsFrame && IsPlainName(expression))
            {
                var found = await FindLocalAsync(client, localsFrame, expression, IsBasicSource(sourcePath), ct).ConfigureAwait(false);

                if (generation != Volatile.Read(ref _stopGeneration) || !IsPaused) return null;
                if (found.Found) return found.Value;
                if (found.Ambiguous) return null;
            }

            // The readings are tried in turn: the evaluator refuses the ones that do not fit the program
            // ("numbers(1)" as a call when numbers is an array), and the first it accepts is the answer.
            foreach (var form in EvaluatorForms(expression, sourcePath))
            {
                // Stepped, resumed or stopped elsewhere since the frames were read: the id names another place.
                if (generation != Volatile.Read(ref _stopGeneration) || !IsPaused) return null;

                var arguments = new JsonObject
                {
                    ["expression"] = form,
                    ["context"] = "watch"
                };

                if (frameId is { } id) arguments["frameId"] = id;

                var response = await client.SendAsync("evaluate", arguments, ct).ConfigureAwait(false);

                if (response["success"]?.GetValue<bool>() == true)
                {
                    var result = response["body"]?["result"]?.GetValue<string>();

                    return result is not null && IsBasicSource(sourcePath) ? VbDisplay.Value(result) : result;
                }

                if (!WorthAnotherReading(form, response, mayRunCodeOnce: false)) return null;
            }

            return null;
        }
        catch (IOException)
        {
            // The session ended while the expression was being evaluated.
            return null;
        }
    }

    /// <summary>
    /// Evaluates a line from the Immediate window, in the "repl" context.
    ///
    /// "repl" rather than "watch": a watch is an expression the debugger may
    /// refuse to let change anything, while the Immediate window is where one
    /// assigns and calls. The frame is looked up here from a fresh stack
    /// trace instead of the shared list of ids, because this runs on whatever
    /// thread the Immediate window used and the list is rewritten by others.
    /// </summary>
    public async Task<ReplEvaluation> EvaluateReplAsync(
        string text, bool isExpression, int frameIndex, CancellationToken ct = default)
    {
        // Only values: a Visual Basic statement cannot be handed to an
        // evaluator that speaks C# without being translated, and a wrong
        // translation would change the program being debugged. Said plainly
        // instead.
        if (!isExpression) return new ReplEvaluation(false, Localizer.Get(StringKeys.ImmediateNoStatements));

        var client = _client;
        var generation = Volatile.Read(ref _stopGeneration);

        if (client is null || !IsPaused || !IsStopShown) return new ReplEvaluation(false, "");

        try
        {
            var trace = await client.SendAsync("stackTrace", new JsonObject
            {
                ["threadId"] = _currentThread
            }, ct).ConfigureAwait(false);

            var frames = trace["body"]?["stackFrames"] as JsonArray;
            var frame = frames is not null && frameIndex >= 0 && frameIndex < frames.Count
                ? frames[frameIndex] as JsonObject
                : null;

            // The evaluator speaks C#: a value asked for in a Visual Basic file is tried in each of its C#
            // readings, as a watch is.
            var sourcePath = SourcePathOf(frame);
            string? firstRefusal = null;

            foreach (var form in EvaluatorForms(text, sourcePath))
            {
                var arguments = new JsonObject
                {
                    ["expression"] = form,
                    ["context"] = "repl"
                };

                if (frame?["id"]?.GetValue<int>() is { } frameId)
                {
                    // Unknown at an exception stop: the frame named nothing the adapter can be asked about.
                    if (await FrameIdShiftAsync(client, frames!, generation, ct).ConfigureAwait(false) is not { } shift)
                        return new ReplEvaluation(false, "");

                    arguments["frameId"] = frameId + shift;

                    // As for a watch: a plain name is a local first, hoisted or not.
                    if (firstRefusal is null && IsPlainName(text)
                        && await FindLocalAsync(client, frameId + shift, text, IsBasicSource(sourcePath), ct).ConfigureAwait(false) is { } local
                        && (local.Found || local.Ambiguous))
                    {
                        return generation != Volatile.Read(ref _stopGeneration) || !IsPaused
                            ? new ReplEvaluation(false, Localizer.Get(StringKeys.ImmediateStale))
                            : new ReplEvaluation(local.Found, local.Value ?? "");
                    }
                }

                // The frame id belongs to the stop it was read in. If the program
                // ran on while the stack was fetched, the id names nothing (or
                // another frame) and the answer would be about the wrong place.
                if (generation != Volatile.Read(ref _stopGeneration) || !IsPaused || !IsStopShown)
                    return new ReplEvaluation(false, Localizer.Get(StringKeys.ImmediateStale));

                var response = await client.SendAsync("evaluate", arguments, ct).ConfigureAwait(false);

                if (response["success"]?.GetValue<bool>() == true)
                {
                    var result = response["body"]?["result"]?.GetValue<string>() ?? "";

                    return new ReplEvaluation(true, IsBasicSource(sourcePath) ? VbDisplay.Value(result) : result);
                }

                // The likeliest reading's refusal is the one worth showing; the others are guesses that failed too.
                firstRefusal ??= response["message"]?.GetValue<string>() ?? "";

                if (!WorthAnotherReading(form, response, mayRunCodeOnce: true)) break;
            }

            return new ReplEvaluation(false, firstRefusal ?? "");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException
                                      || (ex is InvalidOperationException && SessionEnded(client)))
        {
            // The session ended while the line was being evaluated. A client
            // disposed by Stop throws InvalidOperationException (its process
            // is gone), not the IOException of a closed pipe; the same type
            // is also what a malformed answer throws while it is read, and
            // that one is a bug to see, so only the first is caught.
            return new ReplEvaluation(false, Localizer.Get(StringKeys.ImmediateSessionEnded));
        }
    }

    /// <summary>Whether the connection a request was made on has been stopped or has died.</summary>
    private bool SessionEnded(IDapConnection client)
    {
        if (!ReferenceEquals(_client, client)) return true;

        try
        {
            return !client.IsRunning;
        }
        catch (InvalidOperationException)
        {
            // Asking a disposed client whether it runs is itself the answer.
            return true;
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
        Interlocked.Increment(ref _stopGeneration);
        IsPaused = false;
    }

    private void OnEvent(object? sender, DapEvent dapEvent)
    {
        switch (dapEvent.Name)
        {
            case "stopped":
                _currentThread = dapEvent.Body?["threadId"]?.GetValue<int>() ?? 0;
                _stopReason = dapEvent.Body?["reason"]?.GetValue<string>();

                // Whatever was thrown at the last stop is not what this one is about.
                StoppedException = null;
                Interlocked.Increment(ref _stopGeneration);
                IsPaused = true;
                _ = OnStoppedAsync();
                break;

            case "continued":
                Interlocked.Increment(ref _stopGeneration);
                IsPaused = false;
                Resumed?.Invoke(this, EventArgs.Empty);
                break;

            // The program is up. For anything that does not exit by itself —
            // a window, a web host — this is the only word the adapter ever
            // sends to say so, and without it the interface cannot tell
            // "still starting" from "running fine".
            case "process":
                Started?.Invoke(this, EventArgs.Empty);
                break;

            // Which thread to name when breaking in. Only a "stopped" event
            // said so before, so before the first stop there was no thread to
            // name and netcoredbg refused the request outright — the one
            // moment breaking in is the only way to stop at all.
            case "thread":
                if (dapEvent.Body?["threadId"]?.GetValue<int>() is { } thread
                    && dapEvent.Body?["reason"]?.GetValue<string>() == "started"
                    && _currentThread == 0)
                {
                    _currentThread = thread;
                }
                break;

            case "exited":
                Interlocked.Increment(ref _stopGeneration);
                IsPaused = false;
                if (MarkExited()) Exited?.Invoke(this, dapEvent.Body?["exitCode"]?.GetValue<int>() ?? 0);
                break;

            case "output":
                if (dapEvent.Body?["output"]?.GetValue<string>() is { } text)
                {
                    OutputReceived?.Invoke(this, text);

                    // DAP says an event without a category is "console".
                    if (dapEvent.Body?["category"]?.GetValue<string>() is null or "console")
                        DebugOutputReceived?.Invoke(this, text);
                }
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
        // The stop this call is about, before anything can move the counter.
        var generation = Volatile.Read(ref _stopGeneration);

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
                Interlocked.Increment(ref _stopGeneration);
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

            // Read before the stop is shown, so the interface finds it when it is told.
            StoppedException = _stopReason == "exception" ? await ReadExceptionAsync().ConfigureAwait(false) : null;

            Volatile.Write(ref _shownGeneration, generation);
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

    /// <summary>
    /// Whether, after a reading was refused, the next one may be sent.
    /// </summary>
    /// <param name="mayRunCodeOnce">
    /// True for the Immediate window, where a call is something the user does to the program: no reading
    /// follows one that calls something, which may have run before the refusal ("!Compute(n)" ran Compute,
    /// measured). A watch or a hover reads, and is read again at every step anyway, so it tries every reading.
    /// </param>
    /// <remarks>
    /// Never after a time-out, which netcoredbg says leaves the program in an inconsistent state: the next
    /// reading would hang the same way.
    /// </remarks>
    private static bool WorthAnotherReading(string form, JsonNode response, bool mayRunCodeOnce)
    {
        var message = response["message"]?.GetValue<string>() ?? "";

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return false;

        return !mayRunCodeOnce || !VbExpressionTranslator.Calls(form);
    }

    /// <summary>
    /// The value of a local by name, as shown: Visual Basic matches names whatever their case and calls "this"
    /// Me; any other language matches exactly. Null when the frame has no such local.
    /// </summary>
    private static async Task<LocalLookup> FindLocalAsync(IDapConnection client, int frameId, string name, bool basic, CancellationToken ct)
    {
        var locals = await ReadLocalsAsync(client, frameId, ct).ConfigureAwait(false);
        var wanted = name.Trim();

        var matches = basic
            ? locals.Where(v => VbDisplay.Name(v.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList()
            : locals.Where(v => v.Name.Equals(wanted, StringComparison.Ordinal)).ToList();

        // Two locals of one name (hoisted from two loops): which one is meant cannot be told, so neither is said.
        if (matches.Count > 1) return new LocalLookup(Found: false, Ambiguous: true, null);

        if (matches.Count == 1)
            return new LocalLookup(true, false, basic ? VbDisplay.Value(matches[0].Value) : matches[0].Value);

        // "Me" in a Module, a Shared method, or a lambda capturing nothing: there is none, and the evaluator's
        // "this" would be the state machine or the closure the compiler made.
        var noMe = basic && wanted.Equals("Me", StringComparison.OrdinalIgnoreCase);

        return new LocalLookup(Found: false, Ambiguous: noMe, null);
    }

    /// <summary>What a plain name came to among the locals: found, unanswerable (ambiguous or absent Me), or not a local.</summary>
    private sealed record LocalLookup(bool Found, bool Ambiguous, string? Value);

    /// <summary>Whether an expression is one identifier, as a hover or a simple watch sends.</summary>
    private static bool IsPlainName(string expression)
    {
        var text = expression.Trim();

        return text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_') && text.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>The exception stopped on, as "Type: message", or null when the adapter cannot say.</summary>
    private async Task<string?> ReadExceptionAsync()
    {
        if (_client is not { } client) return null;

        // Bounded: the stop is shown after this, and an adapter that never answers must not keep it hidden.
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        JsonObject response;

        try
        {
            response = await client.SendAsync("exceptionInfo", new JsonObject
            {
                ["threadId"] = _currentThread
            }, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (response["success"]?.GetValue<bool>() != true) return null;

        var details = response["body"]?["details"];
        var type = details?["fullTypeName"]?.GetValue<string>() ?? response["body"]?["exceptionId"]?.GetValue<string>();
        var message = details?["message"]?.GetValue<string>();

        return type is null ? response["body"]?["description"]?.GetValue<string>()
            : message is null ? type
            : $"{type}: {message}";
    }

    /// <summary>Whether this stop is an early pass of a counted breakpoint.</summary>
    private bool ShouldSkip(StackFrame frame)
    {
        if (frame.FilePath is not { Length: > 0 } path) return false;

        var file = AsGiven(path)!;

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
