using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>
/// An assistant that plays back prepared turns and records what it was asked.
/// No network, no vendor: what the loop and the tools do with the turns is
/// what is under test.
/// </summary>
internal sealed class ScriptedBackend : IAiChatBackend
{
    private readonly Queue<AiTurnEvent[]> _turns;

    public ScriptedBackend(params AiTurnEvent[][] turns) => _turns = new(turns);

    public List<AiTurnRequest> Requests { get; } = [];

    public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
        AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add(request);

        foreach (var update in _turns.Dequeue())
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public void Dispose() { }

    public static AiTurnEvent[] Say(string text, AiStopReason stop = AiStopReason.EndTurn) =>
        [new AiTextDelta(text), new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart(text)]), stop)];

    public static AiTurnEvent[] Call(string text, string id, string name, JsonObject input) =>
    [
        new AiTextDelta(text),
        new AiTurnEnd(new AiMessage(AiRole.Assistant,
            [new AiTextPart(text), new AiToolCallPart(id, name, input)]), AiStopReason.ToolUse)
    ];
}

/// <summary>A solution folder for the tools, with the editors' buffers faked.</summary>
internal sealed class FakeHost : IAiWorkspaceHost, IDisposable
{
    public FakeHost(string? root = null)
    {
        // Resolved: on macOS the temp folder is itself behind a symbolic link.
        RootDirectory = root ?? AiWorkspaceSandbox.RealPath(
            Directory.CreateTempSubdirectory("basalt-ai-").FullName);
    }

    public string RootDirectory { get; }

    public Dictionary<string, string> Buffers { get; } = [];

    public List<AiDiagnosticInfo> Diagnostics { get; } = [];

    public List<AiSymbolInfo> Symbols { get; } = [];

    public string Write(string relative, string text)
    {
        var path = Path.Combine(RootDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public Task<IReadOnlyDictionary<string, string>> SnapshotOpenBuffersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Buffers));

    public Task<IReadOnlyList<AiDiagnosticInfo>> GetDiagnosticsAsync(string? fullPath, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AiDiagnosticInfo>>(
            [.. Diagnostics.Where(d => fullPath is null || d.FilePath == fullPath)]);

    public Task<IReadOnlyList<AiSymbolInfo>> FindSymbolsAsync(string name, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AiSymbolInfo>>(
            [.. Symbols.Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))]);

    public void Dispose()
    {
        try { Directory.Delete(RootDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
