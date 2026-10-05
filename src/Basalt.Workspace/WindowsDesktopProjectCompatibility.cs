using System.Text.RegularExpressions;
using System.Xml.Linq;
using Basalt.Core.Platform;

namespace Basalt.Workspace;

/// <summary>
/// Lets Basalt open, build and run Visual Basic WinForms projects made by
/// Visual Studio on Windows, on macOS and Linux, without touching the user's
/// project files.
///
/// Such a project targets <c>net6.0-windows</c> and sets
/// <c>UseWindowsForms</c>; the SDK refuses it off Windows. Basalt.Forms
/// implements the same API on plain <c>net10.0</c>, so at build time the
/// project is retargeted onto it through MSBuild global properties that name
/// three files shipped beside the IDE (<c>WindowsDesktop/</c>):
/// <list type="bullet">
/// <item>the props file isolates <c>obj</c> and <c>bin</c> to <c>obj/basalt</c>
/// and <c>bin/basalt</c> for every project outside the Basalt source tree;</item>
/// <item>the retarget file maps any project that uses Windows Forms or targets a
/// <c>-windows</c> framework to <c>net10.0</c>;</item>
/// <item>the targets file references Basalt.Forms and drops the Windows-only
/// System.Drawing.Common package, which would make System.Drawing ambiguous.</item>
/// </list>
/// The same properties must reach restore, build, run (so <c>dotnet run
/// --no-build</c> looks in <c>bin/basalt</c>) and Roslyn's design-time load.
/// On Windows nothing is returned: the real WinForms is used.
///
/// Known gap: whether a project needs the retargeting is judged on the project
/// XML (and the projects it references) alone. <c>UseWindowsForms</c> or a
/// <c>-windows</c> framework that comes only from a Directory.Build.props or
/// another import is not seen.
/// </summary>
public static partial class WindowsDesktopProjectCompatibility
{
    /// <summary>The folder, beside the application, that holds the MSBuild files.</summary>
    public static string FilesDirectory => Path.Combine(AppContext.BaseDirectory, "WindowsDesktop");

    /// <summary>
    /// The global properties that retarget the projects of the solution or
    /// project at <paramref name="path"/>; empty on Windows, when the path holds
    /// no Windows desktop project, or when the Basalt.Forms sources cannot be found.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetGlobalProperties(string path) =>
        Resolve(path).Properties;

    /// <summary>
    /// Like <see cref="GetGlobalProperties(string)"/>, and also says when the
    /// retargeting is needed but cannot be done because the Basalt.Forms
    /// sources are missing. Reads the solution and project files and walks the
    /// disk: call it off the interface thread (see
    /// <see cref="WindowsDesktopCompatibilitySession"/>, which does and caches).
    /// </summary>
    public static WindowsDesktopRetargeting Resolve(string path) =>
        Resolve(path, OperatingSystem.IsWindows(), BasaltSourceRoot.TryFind());

    internal static IReadOnlyDictionary<string, string> GetGlobalProperties(
        string path, bool isWindows, string? sourceRoot) =>
        Resolve(path, isWindows, sourceRoot).Properties;

    internal static WindowsDesktopRetargeting Resolve(string path, bool isWindows, string? sourceRoot)
    {
        if (isWindows || string.IsNullOrWhiteSpace(path)) return WindowsDesktopRetargeting.None;

        var props = Path.Combine(FilesDirectory, "Basalt.WindowsDesktop.props");
        var retarget = Path.Combine(FilesDirectory, "Basalt.WindowsDesktop.Retarget.targets");
        var targets = Path.Combine(FilesDirectory, "Basalt.WindowsDesktop.targets");
        if (!File.Exists(props) || !File.Exists(retarget) || !File.Exists(targets))
            return WindowsDesktopRetargeting.None;

        if (string.IsNullOrEmpty(sourceRoot)
            || !File.Exists(Path.Combine(Path.GetFullPath(sourceRoot), "Basalt.Forms", "Basalt.Forms.csproj")))
        {
            // Without the sources there is nothing to retarget onto, but the
            // user still has to be told why the build will fail.
            return NeedsRetargeting(path, sourceRoot: null)
                ? new WindowsDesktopRetargeting(Empty, SourcesMissing: true)
                : WindowsDesktopRetargeting.None;
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        if (!NeedsRetargeting(path, sourceRoot)) return WindowsDesktopRetargeting.None;

        return new WindowsDesktopRetargeting(new Dictionary<string, string>
        {
            ["DirectoryBuildPropsPath"] = props,
            ["BeforeMicrosoftNETSdkTargets"] = retarget,
            ["CustomAfterMicrosoftCommonTargets"] = targets,
            ["BasaltSourceRoot"] = sourceRoot,
        }, SourcesMissing: false);
    }

    /// <summary>
    /// Properties as <c>-p:Name=Value</c> arguments for the dotnet CLI.
    /// The command line unescapes MSBuild escapes, so <c>%</c>, <c>;</c> and
    /// <c>,</c> in a value (a path may hold any of them) are escaped.
    /// </summary>
    public static IReadOnlyList<string> ToArguments(IReadOnlyDictionary<string, string> properties) =>
        [.. properties.Select(p => $"-p:{p.Key}={Escape(p.Value)}")];

    /// <summary>
    /// True when the solution or project at <paramref name="path"/> has a project
    /// that needs the retargeting, judged on the project XML alone (so the
    /// answer does not depend on the operating system).
    /// </summary>
    public static bool NeedsRetargeting(string path) => NeedsRetargeting(path, sourceRoot: null);

    private static bool NeedsRetargeting(string path, string? sourceRoot)
    {
        var visited = new HashSet<string>(PathComparison.Comparer);
        foreach (var project in ProjectsOf(path))
            if (IsWindowsDesktop(project, sourceRoot, visited)) return true;
        return false;
    }

    /// <summary>The projects a solution lists, or the project itself.</summary>
    private static IEnumerable<string> ProjectsOf(string path)
    {
        var full = Path.GetFullPath(path);
        var extension = Path.GetExtension(full);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)) return SolutionProjects(full);
        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)) return SlnxProjects(full);
        return [full];
    }

    private static IEnumerable<string> SolutionProjects(string solution)
    {
        string text;
        try { text = File.ReadAllText(solution); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        var directory = Path.GetDirectoryName(solution)!;
        foreach (Match match in SolutionProjectLine().Matches(text))
            yield return Resolve(directory, match.Groups["path"].Value);
    }

    private static IEnumerable<string> SlnxProjects(string solution)
    {
        XDocument document;
        try { document = XDocument.Load(solution); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(solution)!;
        foreach (var project in document.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var relative = (string?)project.Attribute("Path");
            if (!string.IsNullOrEmpty(relative)) yield return Resolve(directory, relative);
        }
    }

    // Solution files written on Windows use backslashes.
    private static string Resolve(string directory, string relative) =>
        Path.GetFullPath(Path.Combine(directory, relative.Replace('\\', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Whether a project file uses Windows Forms, targets a <c>-windows</c>
    /// framework or uses the WindowsDesktop SDK, or references a project that does.
    /// Conditions are ignored: a Visual Studio project that mentions any of
    /// these in a configuration still cannot build here without the retargeting.
    /// </summary>
    private static bool IsWindowsDesktop(string projectPath, string? sourceRoot, HashSet<string> visited)
    {
        if (!projectPath.EndsWith("proj", StringComparison.OrdinalIgnoreCase)) return false;
        if (!visited.Add(projectPath) || !File.Exists(projectPath)) return false;
        if (IsUnder(projectPath, sourceRoot)) return false;

        XDocument project;
        try { project = XDocument.Load(projectPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        var root = project.Root;
        if (root is null) return false;

        if (((string?)root.Attribute("Sdk"))?.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        foreach (var element in root.Descendants())
        {
            var name = element.Name.LocalName;
            var value = element.Value;
            if (name == "Sdk" && ((string?)element.Attribute("Name"))?.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (name == "UseWindowsForms" && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name is "TargetFramework" or "TargetFrameworks"
                && value.Contains("-windows", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // A console project that uses a -windows library needs the retargeting
        // too: the library is built as net10.0 and the references must agree.
        var directory = Path.GetDirectoryName(projectPath)!;
        foreach (var reference in root.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = (string?)reference.Attribute("Include");
            if (string.IsNullOrEmpty(include) || include.Contains("$(", StringComparison.Ordinal)) continue;
            if (IsWindowsDesktop(Resolve(directory, include), sourceRoot, visited)) return true;
        }
        return false;
    }

    private static bool IsUnder(string path, string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison.Comparison);
    }

    /// <summary>
    /// MSBuild escaping. The percent sign goes first, or the escapes added for
    /// the other characters would be escaped again.
    /// </summary>
    internal static string Escape(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    [GeneratedRegex("""^\s*Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SolutionProjectLine();
}

/// <summary>
/// What the compatibility layer decided for a solution or project: the MSBuild
/// global properties to pass everywhere (empty when nothing is retargeted) and
/// whether the retargeting was needed but impossible.
/// </summary>
/// <param name="Properties">Properties for restore, build, clean, run, test and Roslyn.</param>
/// <param name="SourcesMissing">
/// A Windows desktop project is present, but the Basalt.Forms sources were not
/// found, so the build will fail with NETSDK1100 unless the user sets
/// <c>BASALT_FORMS_PATH</c>.
/// </param>
public sealed record WindowsDesktopRetargeting(
    IReadOnlyDictionary<string, string> Properties, bool SourcesMissing)
{
    public static WindowsDesktopRetargeting None { get; } =
        new(new Dictionary<string, string>(), SourcesMissing: false);

    /// <summary>The properties as dotnet CLI arguments.</summary>
    public IReadOnlyList<string> Arguments => WindowsDesktopProjectCompatibility.ToArguments(Properties);
}

/// <summary>
/// How paths compare on this system: exactly on a case-sensitive file system
/// (Linux), ignoring case on the two whose default file systems do (Windows,
/// macOS). The MSBuild props file makes the same choice for the source-root test.
/// </summary>
internal static class PathComparison
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        Comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
