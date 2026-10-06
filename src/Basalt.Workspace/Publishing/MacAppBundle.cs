using System.Xml.Linq;

namespace Basalt.Workspace.Publishing;

/// <summary>
/// Wraps a single-file publish into a minimal <c>&lt;Name&gt;.app</c> so that
/// Finder launches a graphical program as an application instead of opening
/// Terminal behind it.
///
/// What is done: <c>Contents/MacOS</c> holds the published files,
/// <c>Contents/Info.plist</c> names the executable, identifier, name and
/// version, and an <c>.icns</c> ApplicationIcon goes to
/// <c>Contents/Resources</c>. What is not done: signing and notarization (the
/// SDK ad-hoc signs an osx-arm64 file only when publishing on macOS; anything
/// else needs <c>codesign</c> by the user before Gatekeeper will run it), the
/// conversion of an .ico or .png into .icns, and entitlements.
/// </summary>
public static class MacAppBundle
{
    /// <summary>
    /// Builds the bundle inside <paramref name="publishDirectory"/> and returns
    /// its path. Whatever the publish left (except debug symbols) moves into
    /// the bundle, because a single-file program still looks for its content
    /// files beside itself; an earlier bundle is replaced.
    /// </summary>
    public static string Create(string publishDirectory, PublishProjectInfo project, string executableFileName)
    {
        var bundle = Path.Combine(publishDirectory, project.AssemblyName + ".app");
        var macOs = Path.Combine(bundle, "Contents", "MacOS");

        if (Directory.Exists(bundle)) Directory.Delete(bundle, recursive: true);
        Directory.CreateDirectory(macOs);

        foreach (var entry in Directory.EnumerateFileSystemEntries(publishDirectory).ToList())
        {
            if (string.Equals(entry, bundle, StringComparison.Ordinal)) continue;
            if (entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) continue;

            var destination = Path.Combine(macOs, Path.GetFileName(entry));
            if (Directory.Exists(entry)) Directory.Move(entry, destination);
            else File.Move(entry, destination, overwrite: true);
        }

        MakeExecutable(Path.Combine(macOs, executableFileName));

        string? iconFile = null;
        if (project.ApplicationIcon is { } icon)
        {
            var source = Path.GetFullPath(Path.Combine(project.ProjectDirectory, icon));
            if (source.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) && File.Exists(source))
            {
                var resources = Path.Combine(bundle, "Contents", "Resources");
                Directory.CreateDirectory(resources);
                iconFile = Path.GetFileName(source);
                File.Copy(source, Path.Combine(resources, iconFile), overwrite: true);
            }
        }

        File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"),
            InfoPlist(project, executableFileName, iconFile));

        return bundle;
    }

    /// <summary>The Info.plist text: the keys Finder needs to run the program.</summary>
    public static string InfoPlist(PublishProjectInfo project, string executableFileName, string? iconFile)
    {
        var entries = new List<(string Key, XElement Value)>
        {
            ("CFBundleExecutable", new XElement("string", executableFileName)),
            ("CFBundleIdentifier", new XElement("string", BundleIdentifier(project))),
            ("CFBundleName", new XElement("string", project.AssemblyName)),
            ("CFBundleDisplayName", new XElement("string", project.AssemblyName)),
            ("CFBundlePackageType", new XElement("string", "APPL")),
            ("CFBundleShortVersionString", new XElement("string", NumericVersion(project.Version))),
            ("CFBundleVersion", new XElement("string", NumericVersion(project.Version))),
            // .NET 10 supports macOS 13 (Ventura) and later; an older system
            // would otherwise start the program and fail inside the runtime.
            ("LSMinimumSystemVersion", new XElement("string", MinimumMacOsVersion)),
            ("NSHighResolutionCapable", new XElement("true"))
        };

        if (iconFile is not null)
            entries.Add(("CFBundleIconFile", new XElement("string", iconFile)));

        var dictionary = new XElement("dict");
        foreach (var (key, value) in entries)
        {
            dictionary.Add(new XElement("key", key));
            dictionary.Add(value);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), dictionary));

        return document.Declaration + Environment.NewLine + document.DocumentType + Environment.NewLine
            + document.Root + Environment.NewLine;
    }

    /// <summary>The oldest macOS .NET 10 runs on, written to LSMinimumSystemVersion.</summary>
    public const string MinimumMacOsVersion = "13.0";

    /// <summary>
    /// Apple wants major.minor.patch of digits for the bundle version: a
    /// prerelease label ("1.2.3-beta.4") or build metadata ("+abc") would make
    /// Finder and the App Store tooling reject or mis-sort it. Missing parts
    /// become 0; anything unreadable becomes 1.0.0.
    /// </summary>
    public static string NumericVersion(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version.Trim(), @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!match.Success) return "1.0.0";

        return $"{match.Groups[1].Value}.{(match.Groups[2].Success ? match.Groups[2].Value : "0")}.{(match.Groups[3].Success ? match.Groups[3].Value : "0")}";
    }

    /// <summary>
    /// A reverse-DNS identifier: <c>com.&lt;company&gt;.&lt;RootNamespace or
    /// AssemblyName&gt;</c> when the project has a Company, otherwise
    /// <c>local.basalt.&lt;name&gt;</c> (the "local" prefix is not a registered
    /// domain, so it cannot collide with someone else's app). Characters Apple
    /// does not allow are dropped.
    /// </summary>
    public static string BundleIdentifier(PublishProjectInfo project)
    {
        var name = Sanitise(project.RootNamespace, allowDots: true);
        if (name.Length == 0) name = Sanitise(project.AssemblyName, allowDots: true);
        if (name.Length == 0) name = "program";

        var company = Sanitise(project.Company, allowDots: false).ToLowerInvariant();
        return (company.Length > 0 ? "com." + company : "local.basalt") + "." + name;
    }

    private static string Sanitise(string? text, bool allowDots) =>
        new([.. (text ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || (allowDots && c == '.'))]);

    /// <summary>Marks a file runnable; a no-op on Windows, which has no such bit.</summary>
    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
