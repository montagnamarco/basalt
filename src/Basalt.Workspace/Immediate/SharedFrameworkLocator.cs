using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Basalt.Workspace.Immediate;

/// <summary>An installed Microsoft.NETCore.App: its folder and exact version.</summary>
public sealed record InstalledRuntime(string Folder, string Version)
{
    public int Major => int.Parse(Version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture);
    public bool IsPreview => Version.Contains('-');
}

/// <summary>
/// Finds the installed .NET shared framework a line is compiled against and
/// run on.
///
/// Not the IDE's own runtime folder: a self-contained IDE (the macOS app
/// bundle, a published build) carries its own copy inside its own folder,
/// which is neither what the child process runs on nor something to compile
/// the user's code against. The runtime the child gets is one <c>dotnet</c>
/// reports.
///
/// Compiled against and run on the same runtime, and that runtime is the
/// project's own major version: a line compiled against System.Runtime 11
/// and run on 10 fails on whatever 11 added. A release is preferred to a
/// preview of any major, and a preview is used only when no release of the
/// wanted major exists.
/// </summary>
public static partial class SharedFrameworkLocator
{
    // "Microsoft.NETCore.App 10.0.0 [/usr/local/share/dotnet/shared/Microsoft.NETCore.App]"
    [GeneratedRegex(@"^Microsoft\.NETCore\.App\s+(?<version>\S+)\s+\[(?<root>[^\]]+)\]\s*$")]
    private static partial Regex RuntimeLine { get; }

    [GeneratedRegex(@"(?<![A-Za-z])net(?<major>\d+)\.\d+")]
    private static partial Regex MonikerPattern { get; }

    private static readonly Lazy<string?> Listing = new(ListRuntimes);

    /// <summary>The runtime to compile against and run on for a project of this major (null: newest release).</summary>
    public static InstalledRuntime? Find(int? major) => Choose(Listing.Value ?? "", major);

    /// <summary>The runtime <see cref="Find"/> chooses with no project, for the tests.</summary>
    public static string? Folder => Find(null)?.Folder;

    /// <summary>
    /// Picks a runtime from the output of <c>dotnet --list-runtimes</c>.
    ///
    /// The wanted major when it is installed (its newest release, else its
    /// newest preview); otherwise the newest release of any major, else the
    /// newest preview.
    /// </summary>
    public static InstalledRuntime? Choose(string listRuntimesOutput, int? major)
    {
        var installed = Parse(listRuntimesOutput);

        if (major is { } wanted)
        {
            var ofMajor = installed.Where(r => r.Major == wanted).ToList();

            if (ofMajor.Count > 0) return Newest(ofMajor);
        }

        return Newest(installed);
    }

    private static InstalledRuntime? Newest(IReadOnlyList<InstalledRuntime> runtimes)
    {
        var pool = runtimes.Any(r => !r.IsPreview) ? runtimes.Where(r => !r.IsPreview) : runtimes;

        return pool.OrderByDescending(r => System.Version.Parse(r.Version.Split('-')[0])).FirstOrDefault();
    }

    private static List<InstalledRuntime> Parse(string output)
    {
        var found = new List<InstalledRuntime>();

        foreach (var line in output.Split('\n'))
        {
            var match = RuntimeLine.Match(line.TrimEnd('\r'));

            if (!match.Success) continue;

            var version = match.Groups["version"].Value;
            var folder = Path.Combine(match.Groups["root"].Value, version);

            if (System.Version.TryParse(version.Split('-')[0], out _) && System.IO.Directory.Exists(folder))
                found.Add(new InstalledRuntime(folder, version));
        }

        return found;
    }

    /// <summary>
    /// The major version of .NET a built project targets: from its runtimeconfig
    /// beside the assembly, else from its project file; null when neither says.
    /// </summary>
    public static int? MajorOf(string? assemblyPath, string? projectPath)
    {
        try
        {
            if (assemblyPath is not null)
            {
                var config = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

                if (File.Exists(config) && FrameworkVersionIn(JsonNode.Parse(File.ReadAllText(config))) is { } version
                    && System.Version.TryParse(version.Split('-')[0], out var parsed))
                    return parsed.Major;
            }

            if (projectPath is not null && File.Exists(projectPath))
            {
                var moniker = XDocument.Load(projectPath).Descendants()
                    .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                    .Select(e => MonikerPattern.Match(e.Value))
                    .FirstOrDefault(m => m.Success);

                if (moniker is not null) return int.Parse(moniker.Groups["major"].Value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            // Unreadable: the newest release is the fallback.
        }

        return null;
    }

    /// <summary>The Microsoft.NETCore.App version a runtimeconfig asks for.</summary>
    internal static string? FrameworkVersionIn(JsonNode? config)
    {
        var options = config?["runtimeOptions"];

        if (options?["framework"] is JsonObject single && IsCore(single)) return single["version"]?.GetValue<string>();

        return options?["frameworks"] is JsonArray many
            ? many.OfType<JsonObject>().FirstOrDefault(IsCore)?["version"]?.GetValue<string>()
            : null;
    }

    internal static bool IsCore(JsonObject framework) =>
        framework["name"]?.GetValue<string>() == "Microsoft.NETCore.App";

    private static string? ListRuntimes()
    {
        try
        {
            var startInfo = DotnetProcess.CreateStartInfo();
            startInfo.ArgumentList.Add("--list-runtimes");
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;

            using var process = Process.Start(startInfo);

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(15_000))
            {
                ChildProcessRunner.Kill(process);
                return null;
            }

            return output.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
