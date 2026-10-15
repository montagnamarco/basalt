using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basalt.Workspace.Debugging;

/// <summary>A running .NET process the debugger could attach to.</summary>
/// <param name="Title">The window's title where it has one, else the command line, else empty.</param>
public sealed record DotnetProcessInfo(int ProcessId, string Name, string Title);

/// <summary>
/// Finds the running .NET processes without any package or the debugger: every .NET runtime opens a diagnostics
/// endpoint named after its process id for tools such as dotnet-counters, and the endpoints are what is listed.
/// </summary>
/// <remarks>
/// On Windows the endpoint is a named pipe, <c>dotnet-diagnostic-{pid}</c>; on macOS and Linux a socket file,
/// <c>dotnet-diagnostic-{pid}-{key}-socket</c>, in the temporary directory of the process (TMPDIR, else /tmp).
/// Asking for the modules each process has loaded to spot coreclr would need to read other processes' memory,
/// which macOS refuses without privileges.
///
/// A program started with DOTNET_EnableDiagnostics=0 has no endpoint and is not listed. A socket left behind by a
/// crashed process is dropped when its process id is not alive or belongs to a process that started after the file
/// was made.
/// </remarks>
public static partial class DotnetProcessLister
{
    [GeneratedRegex(@"^dotnet-diagnostic-(?<pid>\d+)(-.+-socket)?$", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointName();

    /// <summary>The .NET processes running now, other than the IDE itself, sorted by name then id. Blocking: call it off the interface thread.</summary>
    public static IReadOnlyList<DotnetProcessInfo> List() => List(EndpointDirectories(), Environment.ProcessId);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    /// <summary>The user the IDE runs as on macOS and Linux; null on Windows, where a pipe of another user cannot be opened anyway.</summary>
    internal static uint? CurrentUserId => OperatingSystem.IsWindows() ? null : GetEffectiveUserId();

    /// <summary>The endpoints looked for in the given directories (or the pipe directory on Windows).</summary>
    internal static IReadOnlyList<DotnetProcessInfo> List(
        IReadOnlyList<string> directories, int excludedProcessId, uint? currentUserId = null)
    {
        // /tmp is shared: the endpoints of other users' programs are there too, and attaching to one is refused.
        currentUserId ??= CurrentUserId;

        var found = new Dictionary<int, DateTime>();

        foreach (var directory in directories)
        {
            foreach (var (processId, created) in Endpoints(directory))
            {
                if (processId == excludedProcessId) continue;

                // The oldest endpoint of a process proves it the best against a reused id.
                if (!found.TryGetValue(processId, out var known) || created < known) found[processId] = created;
            }
        }

        var processes = new List<(int Id, string Name, string WindowTitle)>();

        foreach (var (processId, created) in found)
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                if (process.HasExited) continue;

                // A socket older than the process that has its id now belongs to one that crashed: its id was reused.
                if (!OperatingSystem.IsWindows() && created != DateTime.MinValue
                    && created < process.StartTime.ToUniversalTime() - TimeSpan.FromSeconds(2)) continue;

                processes.Add((processId, process.ProcessName, WindowTitleOf(process)));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                          or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Gone since the endpoint was seen, or not ours to look at.
            }
        }

        var facts = processes.Count > 0 && !OperatingSystem.IsWindows()
            ? Facts([.. processes.Select(p => p.Id)])
            : new Dictionary<int, (uint? Owner, string CommandLine)>();

        return [.. processes
            .Where(p => currentUserId is not { } me
                        || !facts.TryGetValue(p.Id, out var f) || f.Owner is not { } owner || owner == me)
            .Select(p => new DotnetProcessInfo(
                p.Id, p.Name,
                p.WindowTitle.Length > 0 ? p.WindowTitle : facts.GetValueOrDefault(p.Id).CommandLine ?? ""))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ProcessId)];
    }

    private static IReadOnlyList<string> EndpointDirectories()
    {
        if (OperatingSystem.IsWindows()) return [@"\\.\pipe\"];

        var directories = new List<string> { Path.GetTempPath() };

        // A process started with another TMPDIR still puts its socket where its own environment says; /tmp is the
        // default of those started without one.
        if (!directories.Any(d => Path.TrimEndingDirectorySeparator(d) == "/tmp")) directories.Add("/tmp");

        return directories;
    }

    /// <summary>The process ids named by the endpoints in a directory, with when each was made (MinValue for a pipe).</summary>
    private static List<(int ProcessId, DateTime Created)> Endpoints(string directory)
    {
        var endpoints = new List<(int, DateTime)>();
        IEnumerable<string> paths;

        try
        {
            paths = Directory.Exists(directory) || OperatingSystem.IsWindows()
                ? Directory.EnumerateFileSystemEntries(directory, "dotnet-diagnostic-*").ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return endpoints;
        }

        foreach (var path in paths)
        {
            var match = EndpointName().Match(Path.GetFileName(path));

            if (!match.Success || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
                continue;

            var created = DateTime.MinValue;

            if (!OperatingSystem.IsWindows())
            {
                try { created = File.GetCreationTimeUtc(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            endpoints.Add((processId, created));
        }

        return endpoints;
    }

    private static string WindowTitleOf(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            return "";
        }
    }

    /// <summary>The facts of each process read from a /proc tree. One that vanishes while being read is left out and does not end the reading of the others.</summary>
    internal static Dictionary<int, (uint? Owner, string CommandLine)> FactsFromProc(string procRoot, IReadOnlyList<int> processIds)
    {
        var facts = new Dictionary<int, (uint? Owner, string CommandLine)>();

        foreach (var id in processIds)
        {
            var directory = Path.Combine(procRoot, id.ToString(CultureInfo.InvariantCulture));

            try
            {
                uint? owner = null;

                // "Uid:\t1000\t1000\t1000\t1000": the real id first.
                foreach (var line in File.ReadLines(Path.Combine(directory, "status")))
                {
                    if (!line.StartsWith("Uid:", StringComparison.Ordinal)) continue;

                    var fields = line["Uid:".Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length > 0 && uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                        owner = uid;
                    break;
                }

                facts[id] = (owner, File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ').Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone since it was listed, or not ours to read: the others are still looked at.
            }
        }

        return facts;
    }

    /// <summary>The owner and command line of each process; the command line says which program "dotnet" is running.</summary>
    private static Dictionary<int, (uint? Owner, string CommandLine)> Facts(IReadOnlyList<int> processIds)
    {
        var facts = new Dictionary<int, (uint? Owner, string CommandLine)>();

        try
        {
            if (OperatingSystem.IsLinux())
            {
                return FactsFromProc("/proc", processIds);
            }
            else if (OperatingSystem.IsMacOS())
            {
                // One call for all of them; -ww so that a long line is not cut at the terminal's width.
                var startInfo = new ProcessStartInfo("ps")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var argument in new[] { "-ww", "-o", "pid=,uid=,args=", "-p", string.Join(',', processIds) })
                    startInfo.ArgumentList.Add(argument);

                using var ps = Process.Start(startInfo);

                if (ps is null) return facts;

                var output = ps.StandardOutput.ReadToEnd();
                ps.WaitForExit(2000);

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (fields.Length >= 2
                        && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        && uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                        facts[id] = (uid, fields.Length > 2 ? fields[2] : "");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception)
        {
            // The titles and owners help tell processes apart; without them every process is offered.
        }

        return facts;
    }
}
