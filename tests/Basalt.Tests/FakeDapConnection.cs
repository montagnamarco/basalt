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

    public bool IsRunning { get; private set; } = true;

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

        if (command == "stackTrace" && HoldNextStackTrace is { } hold)
        {
            HoldNextStackTrace = null;
            await hold.Task.ConfigureAwait(false);
        }

        var body = command switch
        {
            "stackTrace" => Answer(true, new JsonObject
            {
                ["stackFrames"] = new JsonArray
                {
                    Frame(100, "Main", "/app/Module1.vb", 5),
                    Frame(101, "Caller", "/app/Helpers.cs", 9)
                }
            }),
            "evaluate" => EvaluateAnswer(arguments),
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

    public static JsonObject Answer(bool success, JsonObject body, string? message = null)
    {
        var answer = new JsonObject { ["success"] = success, ["body"] = body };
        if (message is not null) answer["message"] = message;

        return answer;
    }

    private static JsonObject Frame(int id, string name, string path, int line) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["source"] = new JsonObject { ["path"] = path },
        ["line"] = line
    };

    public void Dispose()
    {
        IsRunning = false;
        Exited?.Invoke(this, 0);
    }
}
