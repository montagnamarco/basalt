using System.Text.Json.Nodes;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// A debug adapter that records what it is asked and answers with canned
/// frames, so the requests the IDE builds can be read back without netcoredbg.
/// </summary>
internal sealed class FakeDapConnection : IDapConnection
{
    private readonly object _gate = new();
    private readonly List<(string Command, JsonObject? Arguments)> _requests = [];

    private bool _running = true;

    /// <summary>Makes <see cref="IsRunning"/> throw, as a disposed DapClient does (its process is gone).</summary>
    public bool IsRunningThrows { get; set; }

    public bool IsRunning
    {
        get => IsRunningThrows ? throw new InvalidOperationException("No process is associated with this object.") : _running;
        private set => _running = value;
    }

    /// <summary>The adapter process dies without a word: it answers nothing from now on.</summary>
    public void Kill() => IsRunning = false;

    /// <summary>Holds the answer to "disconnect" until completed; null answers at once.</summary>
    public TaskCompletionSource? HoldDisconnect { get; set; }

    public event EventHandler<DapEvent>? EventReceived;
    public event EventHandler<int>? Exited;

    /// <summary>The thread each request arrived on, in order.</summary>
    public List<int> RequestThreads { get; } = [];

    /// <summary>What "evaluate" answers.</summary>
    public Func<JsonObject?, JsonObject> EvaluateAnswer { get; set; } = _ => Answer(true, new JsonObject { ["result"] = "7" });

    public IReadOnlyList<(string Command, JsonObject? Arguments)> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    public JsonObject? ArgumentsOf(string command)
    {
        lock (_gate) return _requests.LastOrDefault(r => r.Command == command).Arguments;
    }

    /// <summary>Called as each request arrives, before it is answered.</summary>
    public Action<string>? OnRequest { get; set; }

    /// <summary>Whether the frames carry a module id, as netcoredbg's do at every stop but an unhandled exception.</summary>
    public bool FramesHaveModule { get; set; }

    /// <summary>The frames listed, as (name, file, line); ids are numbered from 100.</summary>
    public List<(string Name, string Path, int Line)> Frames { get; set; } =
        [("Main", "/app/Module1.vb", 5), ("Caller", "/app/Helpers.cs", 9)];

    /// <summary>
    /// The variables each frame id holds, for "scopes" and "variables". An id left out is unknown to the
    /// adapter, as netcoredbg answers past the last frame.
    /// </summary>
    public Dictionary<int, string[]> LocalsById { get; set; } = new()
    {
        [100] = ["total"],
        [101] = ["count"],
    };

    /// <summary>What "configurationDone" answers; by default it succeeds.</summary>
    public JsonObject ConfigurationDoneAnswer { get; set; } = Answer(true, new JsonObject());

    /// <summary>"configurationDone" left unanswered, as netcoredbg does when attached to a process that is not .NET.</summary>
    public bool ConfigurationDoneNeverAnswers { get; set; }

    /// <summary>What "attach" answers; by default it succeeds.</summary>
    public JsonObject AttachAnswer { get; set; } = Answer(true, new JsonObject());

    /// <summary>What "setExceptionBreakpoints" answers; by default it succeeds.</summary>
    public JsonObject SetExceptionBreakpointsAnswer { get; set; } = Answer(true, new JsonObject());

    /// <summary>What "exceptionInfo" answers; by default it says nothing.</summary>
    public JsonObject ExceptionInfoAnswer { get; set; } = Answer(false, new JsonObject());

    /// <summary>Holds the answer to the next stackTrace until it is completed.</summary>
    public TaskCompletionSource? HoldNextStackTrace { get; set; }

    public async Task<JsonObject> SendAsync(string command, JsonObject? arguments = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Cloned: the caller may reuse the node, which cannot be parented twice.
            _requests.Add((command, arguments is null ? null : JsonNode.Parse(arguments.ToJsonString()) as JsonObject));
            RequestThreads.Add(Environment.CurrentManagedThreadId);
        }

        OnRequest?.Invoke(command);

        if (command == "disconnect" && HoldDisconnect is { } disconnectHold)
            await disconnectHold.Task.ConfigureAwait(false);

        if (command == "stackTrace" && HoldNextStackTrace is { } hold)
        {
            HoldNextStackTrace = null;
            await hold.Task.ConfigureAwait(false);
        }

        var body = command switch
        {
            "stackTrace" => Answer(true, new JsonObject
            {
                ["stackFrames"] = new JsonArray([.. Frames.Select((f, i) => (JsonNode)Frame(100 + i, f.Name, f.Path, f.Line, FramesHaveModule))])
            }),
            "scopes" => Scopes(arguments!["frameId"]!.GetValue<int>()),
            "variables" => Variables(arguments!["variablesReference"]!.GetValue<int>()),
            "evaluate" => EvaluateAnswer(arguments),
            "exceptionInfo" => ExceptionInfoAnswer,
            "attach" => AttachAnswer,
            "setExceptionBreakpoints" => SetExceptionBreakpointsAnswer,
            "configurationDone" when ConfigurationDoneNeverAnswers => await new TaskCompletionSource<JsonObject>().Task.WaitAsync(ct).ConfigureAwait(false),
            "configurationDone" => ConfigurationDoneAnswer,
            _ => Answer(true, new JsonObject())
        };

        if (command == "initialize") Raise("initialized", new JsonObject());

        return body;
    }

    public void Send(string command, JsonObject? arguments = null)
    {
        lock (_gate) _requests.Add((command, arguments));
    }

    public void Raise(string name, JsonObject body) =>
        EventReceived?.Invoke(this, new DapEvent(name, body));

    private JsonObject Scopes(int frameId) => LocalsById.ContainsKey(frameId)
        ? Answer(true, new JsonObject
        {
            ["scopes"] = new JsonArray(new JsonObject { ["name"] = "Locals", ["variablesReference"] = 1000 + frameId })
        })
        : Answer(false, new JsonObject(), "Failed command 'scopes' : 0x80004005");

    /// <summary>
    /// Children by variables reference, as (name, value, type, child reference), for a local that has them, such
    /// as an Async method's state machine.
    /// </summary>
    public Dictionary<int, (string Name, string Value, string Type, int Reference)[]> ChildrenByReference { get; set; } = [];

    private JsonObject Variables(int reference) => Answer(true, new JsonObject
    {
        ["variables"] = ChildrenByReference.TryGetValue(reference, out var children)
            ? new JsonArray([.. children.Select(c => (JsonNode)new JsonObject
                { ["name"] = c.Name, ["value"] = c.Value, ["type"] = c.Type, ["variablesReference"] = c.Reference })])
            : new JsonArray([.. (LocalsById.TryGetValue(reference - 1000, out var names) ? names : [])
                .Select(name => (JsonNode)new JsonObject { ["name"] = name, ["value"] = "1", ["type"] = "int", ["variablesReference"] = 0 })])
    });

    public static JsonObject Answer(bool success, JsonObject body, string? message = null)
    {
        var answer = new JsonObject { ["success"] = success, ["body"] = body };
        if (message is not null) answer["message"] = message;

        return answer;
    }

    private static JsonObject Frame(int id, string name, string path, int line, bool withModule)
    {
        var frame = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["source"] = new JsonObject { ["path"] = path },
            ["line"] = line
        };

        if (withModule) frame["moduleId"] = "d8564ecd-204f-415c-a399-37394aa31c30";

        return frame;
    }

    public void Dispose()
    {
        IsRunning = false;
        Exited?.Invoke(this, 0);
    }
}
