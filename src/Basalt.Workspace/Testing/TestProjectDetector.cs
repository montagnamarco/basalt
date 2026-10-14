using System.Xml.Linq;

namespace Basalt.Workspace.Testing;

/// <summary>
/// Recognises the projects that hold tests.
///
/// A project is a test project when the MSBuild file says so, not when its
/// name ends in "Tests": that is a convention. The file is read as XML rather
/// than searched for text, because a comment or a property that merely
/// mentions the test SDK is not a reference to it.
/// </summary>
public static class TestProjectDetector
{
    /// <summary>The package every VSTest-based test project references.</summary>
    internal const string TestSdkPackage = "Microsoft.NET.Test.Sdk";

    /// <summary>The SDK prefix of MSTest's own project SDK, which replaces the package.</summary>
    internal const string MSTestSdkPrefix = "MSTest.Sdk";

    /// <summary>The property that asks the build to use the Microsoft Testing Platform runner.</summary>
    internal const string TestingPlatformProperty = "UseMicrosoftTestingPlatformRunner";

    /// <summary>
    /// Whether the project file declares a test project.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read is not a test project: the solution scan
    /// must go on past it rather than stop.
    /// </remarks>
    public static bool IsTestProject(string projectPath)
    {
        XDocument document;

        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        return IsTestProject(document);
    }

    /// <summary>Whether the project file's XML declares a test project.</summary>
    internal static bool IsTestProject(XDocument document)
    {
        var root = document.Root;
        if (root is null) return false;

        // MSTest.Sdk is a project SDK of its own, and the project opts in by naming it.
        var sdk = root.Attribute("Sdk")?.Value;
        if (sdk is not null && sdk.Split(';').Any(IsMSTestSdk)) return true;

        // The explicit flag, which the SDKs read to decide the project is tested.
        if (ElementsNamed(root, "IsTestProject").Any(e => IsTrue(e.Value))) return true;

        return ElementsNamed(root, "PackageReference")
            .Any(e => string.Equals(e.Attribute("Include")?.Value?.Trim(), TestSdkPackage, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the project runs its tests with the Microsoft Testing Platform
    /// rather than the VSTest adapter.
    /// </summary>
    /// <remarks>
    /// The two differ in what "dotnet test" does: under the platform,
    /// "--list-tests" runs the tests instead of listing them, and results are
    /// written by a platform option. Discovery must ask the program directly.
    /// </remarks>
    public static bool UsesTestingPlatform(string projectPath)
    {
        XDocument document;

        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        return UsesTestingPlatform(document);
    }

    internal static bool UsesTestingPlatform(XDocument document)
    {
        var root = document.Root;
        if (root is null) return false;

        if (root.Attribute("Sdk")?.Value is { } sdk && sdk.Split(';').Any(IsMSTestSdk)) return true;

        return ElementsNamed(root, TestingPlatformProperty).Any(e => IsTrue(e.Value));
    }

    /// <summary>
    /// The test projects in a folder tree: the project files that declare
    /// tests, leaving out the build output folders.
    /// </summary>
    public static IEnumerable<string> FindTestProjects(string directory) =>
        ScanForTestProjects(directory, _ => { });

    /// <summary>
    /// Walks the folder tree, leaving the build output folders out of the walk
    /// itself, and returns the test projects it found. <paramref name="entered"/>
    /// is told of each folder the walk descends into.
    /// </summary>
    /// <remarks>
    /// The output folders are skipped while enumerating, not filtered after:
    /// a large solution's bin and obj hold copies of its sources, and walking
    /// them is most of the cost of a scan.
    /// </remarks>
    internal static List<string> ScanForTestProjects(string directory, Action<string> entered)
    {
        var projectFiles = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            entered(folder);

            try
            {
                projectFiles.AddRange(Directory.EnumerateFiles(folder)
                    .Where(p => p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                             || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));

                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    if (IsBuildOutput(child)) continue;

                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read holds no project this run can use.
            }
        }

        return projectFiles.Where(IsTestProject).ToList();
    }

    private static bool IsBuildOutput(string folder)
    {
        var name = Path.GetFileName(folder);

        return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMSTestSdk(string sdk)
    {
        // "MSTest.Sdk/3.9.3": the name is what comes before the version.
        var name = sdk.Trim();
        var slash = name.IndexOf('/');
        if (slash >= 0) name = name[..slash];

        return string.Equals(name, MSTestSdkPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The elements of the given name anywhere in the project, whatever the
    /// XML namespace: older VB project files declare one and newer ones do not.
    /// </summary>
    private static IEnumerable<XElement> ElementsNamed(XElement root, string localName) =>
        root.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static bool IsTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
