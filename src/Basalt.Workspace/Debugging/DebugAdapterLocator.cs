using System.Runtime.InteropServices;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Finds the netcoredbg executable for the machine the IDE is running on.
///
/// The adapter is not part of the .NET SDK, so it is either shipped alongside
/// the IDE or installed by the user. Both are looked for.
/// </summary>
public static class DebugAdapterLocator
{
    /// <summary>The runtime identifier netcoredbg builds are published under.</summary>
    public static string RuntimeIdentifier
    {
        get
        {
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                _ => "x64"
            };

            if (OperatingSystem.IsMacOS()) return $"osx-{architecture}";
            if (OperatingSystem.IsWindows()) return $"win-{architecture}";
            return $"linux-{architecture}";
        }
    }

    private static string ExecutableName =>
        OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg";

    /// <summary>
    /// Where the adapter is, or null if it is not installed.
    ///
    /// An explicit setting wins, then a copy shipped with the IDE, then
    /// whatever is on the PATH.
    /// </summary>
    public static string? Find(string? configuredPath = null)
    {
        if (configuredPath is { Length: > 0 } && File.Exists(configuredPath))
            return configuredPath;

        if (Environment.GetEnvironmentVariable("BASALT_NETCOREDBG") is { Length: > 0 } fromEnvironment
            && File.Exists(fromEnvironment))
            return fromEnvironment;

        foreach (var candidate in ShippedCandidates())
            if (File.Exists(candidate)) return candidate;

        return FindOnPath();
    }

    private static IEnumerable<string> ShippedCandidates()
    {
        var beside = AppContext.BaseDirectory;

        yield return Path.Combine(beside, "debugger", RuntimeIdentifier, ExecutableName);
        yield return Path.Combine(beside, "debugger", ExecutableName);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length == 0) yield break;

        yield return Path.Combine(home, ".basalt", "debugger", RuntimeIdentifier, ExecutableName);
        yield return Path.Combine(home, ".basalt", "debugger", ExecutableName);
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;

            string candidate;
            try
            {
                candidate = Path.Combine(directory, ExecutableName);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is worth skipping, not crashing over.
                continue;
            }

            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
