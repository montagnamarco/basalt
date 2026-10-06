using System.Runtime.InteropServices;

namespace Basalt.Workspace.Publishing;

/// <summary>The operating system a published program runs on.</summary>
public enum PublishOs { Windows, MacOS, Linux }

/// <summary>
/// One runtime identifier the "Make executable" command can produce a program
/// for: the six desktop platforms .NET publishes self-contained.
/// </summary>
public sealed record PublishTarget(string Rid, PublishOs Os, Architecture Architecture)
{
    public static IReadOnlyList<PublishTarget> All { get; } =
    [
        new("win-x64", PublishOs.Windows, Architecture.X64),
        new("win-arm64", PublishOs.Windows, Architecture.Arm64),
        new("osx-arm64", PublishOs.MacOS, Architecture.Arm64),
        new("osx-x64", PublishOs.MacOS, Architecture.X64),
        new("linux-x64", PublishOs.Linux, Architecture.X64),
        new("linux-arm64", PublishOs.Linux, Architecture.Arm64)
    ];

    public static PublishTarget? Find(string rid) =>
        All.FirstOrDefault(t => string.Equals(t.Rid, rid, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The target matching this machine, which is what a developer almost
    /// always wants first; a 32-bit or exotic processor falls back to x64.
    /// </summary>
    public static PublishTarget Current()
    {
        var os = OperatingSystem.IsWindows() ? PublishOs.Windows
            : OperatingSystem.IsMacOS() ? PublishOs.MacOS
            : PublishOs.Linux;

        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? Architecture.Arm64
            : Architecture.X64;

        return All.First(t => t.Os == os && t.Architecture == architecture);
    }

    /// <summary>The name of the program file a single-file publish leaves.</summary>
    public string ExecutableFileName(string assemblyName) =>
        Os == PublishOs.Windows ? assemblyName + ".exe" : assemblyName;
}
