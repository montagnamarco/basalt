namespace Basalt.Workspace.Ai;

/// <summary>A compiler or analyzer finding, as the assistant is told it.</summary>
public sealed record AiDiagnosticInfo(string Id, string Message, string Severity, string? FilePath, int Line, int Column);

/// <summary>A declaration found by name.</summary>
public sealed record AiSymbolInfo(string Name, string Kind, string FilePath, int Line, int Column);

/// <summary>
/// What the assistant's tools need from the running IDE.
///
/// The tools live here, with no editor behind them; the shell supplies the
/// parts only it can answer: the text in open editors, which is newer than the
/// files, and the language service's diagnostics and symbols.
/// </summary>
public interface IAiWorkspaceHost
{
    /// <summary>The opened solution's folder; every tool is confined to it.</summary>
    string RootDirectory { get; }

    /// <summary>
    /// The text of every document open in an editor, by full path.
    ///
    /// Read from the editors rather than from disk: what the user sees is
    /// what the assistant should reason about, saved or not.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> SnapshotOpenBuffersAsync(CancellationToken ct);

    /// <summary>Findings for one file, or for the whole solution when no path is given.</summary>
    Task<IReadOnlyList<AiDiagnosticInfo>> GetDiagnosticsAsync(string? fullPath, CancellationToken ct);

    /// <summary>Declarations whose name matches, found by the language service.</summary>
    Task<IReadOnlyList<AiSymbolInfo>> FindSymbolsAsync(string name, CancellationToken ct);
}
