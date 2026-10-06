using System.Xml.Linq;
using Basalt.Core.Model;
using Basalt.Core.Platform;
using Basalt.Designer;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// How a project gets Basalt.WinForms: from the Basalt sources while they exist,
/// otherwise from the NuGet packages the IDE ships, restored from a folder the
/// IDE passes to restore (so no nuget.org publication is needed and the user's
/// project names no path of this machine).
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsPackageReferenceTests : IDisposable
{
    private const string Version = "9.8.7";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basalt-winforms-package-" + Guid.NewGuid().ToString("N"));

    public WinFormsPackageReferenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // The private NuGet cache links into the user's real cache: remove the
        // links themselves first so no recursive delete can ever follow them.
        var cache = Path.Combine(_root, "nuget-packages");
        if (Directory.Exists(cache))
        {
            foreach (var link in Directory.EnumerateFileSystemEntries(cache))
            {
                if (new DirectoryInfo(link).LinkTarget is not null)
                    Directory.Delete(link);
            }
        }

        WindowsDesktopSamples.DeleteQuietly(_root);
    }

    private static BasaltWinFormsPackages Packages(string source = "/ide/WinFormsPackages") => new(Version, source);

    private string FakeSources()
    {
        var sources = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(sources, "Basalt.Forms"));
        File.WriteAllText(Path.Combine(sources, "Basalt.Forms", "Basalt.Forms.csproj"), "<Project />");
        return sources;
    }

    private string WriteProject(string name, string body)
    {
        var path = Path.Combine(_root, name, name + ".vbproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");
        return path;
    }

    private const string WindowsProject =
        "<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup>";

    // ---- the decision -----------------------------------------------------

    [Fact]
    public void SourcesWinWhenBothExistAndAFolderWithoutBasaltFormsIsNoSource()
    {
        var sources = FakeSources();
        var both = BasaltWinFormsReference.Resolve(sources, Packages());
        Assert.True(both.UsesSources);
        Assert.False(both.UsesPackages);

        var empty = BasaltWinFormsReference.Resolve(_root, Packages());
        Assert.True(empty.UsesPackages);

        var neither = BasaltWinFormsReference.Resolve(null, null);
        Assert.False(neither.IsAvailable);
        Assert.Throws<DirectoryNotFoundException>(() => neither.Require());
    }

    [Fact]
    public void PackagesNeedBothNupkgFilesOfTheBuiltVersion()
    {
        Assert.Null(BasaltWinFormsPackages.TryFind(_root, Version));
        File.WriteAllText(Path.Combine(_root, $"Basalt.WinForms.{Version}.nupkg"), "");
        Assert.Null(BasaltWinFormsPackages.TryFind(_root, Version));
        File.WriteAllText(Path.Combine(_root, $"Basalt.Drawing.{Version}.nupkg"), "");
        Assert.Equal(Path.GetFullPath(_root), BasaltWinFormsPackages.TryFind(_root, Version)?.Source);
        Assert.Null(BasaltWinFormsPackages.TryFind(_root, "1.0.0"));
        Assert.Null(BasaltWinFormsPackages.TryFind(_root, ""));
    }

    [Fact]
    public void TheVersionTheIdeWritesIsTheOneThePackIsVersionedWith()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Directory.Build.props")))
            directory = Path.GetDirectoryName(directory);
        Assert.SkipWhen(directory is null, "Directory.Build.props is not above the test output.");

        var declared = XDocument.Load(Path.Combine(directory!, "Directory.Build.props"))
            .Descendants("WinFormsVersion").Single().Value;
        Assert.Equal(declared, BasaltWinFormsPackages.BuiltVersion);
    }

    // ---- the template ------------------------------------------------------

    [Fact]
    public async Task TemplateWithoutSourcesReferencesThePackageAndWritesNoMachinePath()
    {
        var packageFolder = Path.Combine(_root, "ide-packages");
        var generated = await SolutionTemplates.CreateAsync(
            _root, "FromPackage", ProjectTemplate.WinFormsApp,
            winForms: BasaltWinFormsReference.Resolve(null, Packages(packageFolder)));

        var text = await File.ReadAllTextAsync(generated.ProjectPath);
        var reference = Assert.Single(XDocument.Parse(text).Descendants("PackageReference"));
        Assert.Equal("Basalt.WinForms", (string?)reference.Attribute("Include"));
        Assert.Equal(Version, (string?)reference.Attribute("Version"));
        Assert.DoesNotContain("BasaltFormsPath", text, StringComparison.Ordinal);
        Assert.Empty(XDocument.Parse(text).Descendants("ProjectReference"));
        // Neither the package folder nor any other folder of this machine.
        Assert.DoesNotContain("ide-packages", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreAdditionalProjectSources", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateWithSourcesKeepsTheProjectReferences()
    {
        var sources = FakeSources();
        var generated = await SolutionTemplates.CreateAsync(
            _root, "FromSources", ProjectTemplate.WinFormsApp,
            winForms: BasaltWinFormsReference.Resolve(sources, Packages()));

        var project = XDocument.Load(generated.ProjectPath);
        Assert.Equal(sources, project.Descendants("BasaltFormsPath").Single().Value);
        Assert.Equal(6, project.Descendants("ProjectReference").Count());
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public async Task TemplateWithNeitherSourcesNorPackagesSaysHowToFixIt()
    {
        var error = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => SolutionTemplates.CreateAsync(
            _root, "Nothing", ProjectTemplate.WinFormsApp, winForms: BasaltWinFormsReference.Resolve(null, null)));
        Assert.Contains(BasaltSourceRoot.EnvironmentVariable, error.Message, StringComparison.Ordinal);
        Assert.Contains(BasaltWinFormsPackages.SourceVariable, error.Message, StringComparison.Ordinal);
    }

    // ---- the retargeting ---------------------------------------------------

    [Fact]
    public void RetargetingWithoutSourcesReferencesThePackageAndAddsTheRestoreSource()
    {
        var app = WriteProject("App", WindowsProject);
        var packages = Packages(Path.Combine(_root, "feed"));

        var result = WindowsDesktopProjectCompatibility.Resolve(app, isWindows: false, sourceRoot: null, packages);

        Assert.False(result.SourcesMissing);
        Assert.Equal(
            ["BasaltWinFormsVersion", "BeforeMicrosoftNETSdkTargets", "CustomAfterMicrosoftCommonTargets",
             "DirectoryBuildPropsPath", "RestoreAdditionalProjectSources"],
            result.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Version, result.Properties["BasaltWinFormsVersion"]);
        Assert.Equal(packages.Source, result.Properties["RestoreAdditionalProjectSources"]);
        Assert.Contains($"-p:RestoreAdditionalProjectSources={packages.Source}", result.Arguments);
    }

    [Fact]
    public void RetargetingPrefersTheSourcesAndDoesNotAddPackageProperties()
    {
        var app = WriteProject("App", WindowsProject);
        var result = WindowsDesktopProjectCompatibility.Resolve(app, isWindows: false, FakeSources(), Packages());

        Assert.Contains("BasaltSourceRoot", result.Properties.Keys);
        Assert.DoesNotContain("BasaltWinFormsVersion", result.Properties.Keys);
        Assert.DoesNotContain("RestoreAdditionalProjectSources", result.Properties.Keys);
    }

    [Fact]
    public void SourcesMissingIsReportedOnlyWhenThePackagesAreMissingToo()
    {
        var app = WriteProject("App", WindowsProject);
        var plain = WriteProject("Plain", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");

        Assert.True(WindowsDesktopProjectCompatibility.Resolve(app, false, null, null).SourcesMissing);
        Assert.False(WindowsDesktopProjectCompatibility.Resolve(app, false, null, Packages()).SourcesMissing);
        // A project that needs nothing gets nothing, packages or not.
        Assert.Empty(WindowsDesktopProjectCompatibility.Resolve(plain, false, null, Packages()).Properties);
    }

    [Fact]
    public void ProjectThatReferencesThePackageGetsTheRestoreSourceOnEverySystem()
    {
        var generated = WriteProject("Template", $"""
            <PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><PackageReference Include="Basalt.WinForms" Version="{Version}" /></ItemGroup>
            """);
        var packages = Packages(Path.Combine(_root, "feed"));

        foreach (var isWindows in new[] { true, false })
        {
            var result = WindowsDesktopProjectCompatibility.Resolve(generated, isWindows, null, packages);
            var property = Assert.Single(result.Properties);
            Assert.Equal("RestoreAdditionalProjectSources", property.Key);
            Assert.Equal(packages.Source, property.Value);
        }

        var other = WriteProject("Other", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        Assert.Empty(WindowsDesktopProjectCompatibility.Resolve(other, false, null, packages).Properties);
    }

    // ---- restore from the shipped packages, end to end ------------------------
    //
    // These use the .nupkg files that the build copied beside the tests from
    // artifacts/ (build/pack.sh --winforms-only), and need NuGet for Avalonia
    // like every build test here. They skip when the packages were not packed.

    private static BasaltWinFormsPackages RequireShippedPackages()
    {
        var packages = BasaltWinFormsPackages.TryFind();
        Assert.SkipWhen(packages is null, "No shipped packages: run build/pack.sh --winforms-only, then build again.");
        return packages!;
    }

    private async Task<string> BuildAsync(string projectPath, BasaltWinFormsPackages packages, bool isWindows)
    {
        // A copy left in the global cache by an earlier pack or restore would
        // be used without ever asking the package source, and would hide a
        // restore source that is not passed. The user's cache is never
        // touched: the build gets a private one that links every package
        // already there except Basalt's own, so Avalonia and the rest are not
        // downloaded again but the two Basalt packages must come from the
        // source the IDE ships.
        var previousCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var privateCache = PrivateCacheWithoutBasalt(previousCache);
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", privateCache);

        try
        {
            var builder = new MsBuildBuildService
            {
                Compatibility = new WindowsDesktopCompatibilitySession(path =>
                    WindowsDesktopProjectCompatibility.Resolve(path, isWindows, sourceRoot: null, packages)),
            };
            var output = new System.Text.StringBuilder();
            builder.OutputReceived += (_, line) =>
            {
                lock (output)
                {
                    output.AppendLine(line);
                }
            };
            var result = await builder.BuildAsync(projectPath);
            Assert.True(result.Succeeded, output.ToString());
            return output.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousCache);
        }
    }

    private string PrivateCacheWithoutBasalt(string? userCache)
    {
        if (string.IsNullOrEmpty(userCache))
            userCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var cache = Path.Combine(_root, "nuget-packages");
        Directory.CreateDirectory(cache);

        if (!Directory.Exists(userCache))
            return cache;

        foreach (var package in Directory.EnumerateDirectories(userCache))
        {
            var id = Path.GetFileName(package);
            if (id is "basalt.winforms" or "basalt.drawing")
                continue;

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(cache, id), package);
            }
            catch (IOException)
            {
                // Linking needs a privilege on Windows; without it the restore
                // simply downloads that package into the private cache.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        return cache;
    }

    [Fact]
    public async Task GeneratedProjectRestoresAndBuildsFromTheShippedPackages()
    {
        var packages = RequireShippedPackages();
        var generated = await SolutionTemplates.CreateAsync(
            _root, "Shipped", ProjectTemplate.WinFormsApp,
            winForms: BasaltWinFormsReference.Resolve(null, packages));

        await BuildAsync(generated.ProjectPath, packages, OperatingSystem.IsWindows());

        var assets = Path.Combine(Path.GetDirectoryName(generated.ProjectPath)!, "obj", "project.assets.json");
        Assert.Contains("Basalt.WinForms/" + packages.Version, await File.ReadAllTextAsync(assets), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VisualStudioProjectIsRetargetedOntoTheShippedPackages()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        var packages = RequireShippedPackages();
        var project = WriteProject("VsApp", WindowsProject);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "Form1.vb"), """
            Imports System.Windows.Forms

            Public Class Form1
                Inherits Form

                Public Sub New()
                    Controls.Add(New Button())
                End Sub

                <STAThread>
                Public Shared Sub Main()
                    Application.Run(New Form1())
                End Sub
            End Class
            """);
        var original = await File.ReadAllBytesAsync(project);

        await BuildAsync(project, packages, isWindows: false);

        Assert.Equal(original, await File.ReadAllBytesAsync(project));
        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "basalt", "project.assets.json");
        Assert.Contains("Basalt.WinForms/" + packages.Version, await File.ReadAllTextAsync(assets), StringComparison.Ordinal);
    }
}
