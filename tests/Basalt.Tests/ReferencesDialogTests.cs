using System.Text;
using Avalonia.Headless.XUnit;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Project &gt; References… — checking another project of the solution and
/// clicking OK must leave that reference in the project file on disk;
/// clicking Cancel must leave the file exactly as it was.
/// </summary>
public sealed class ReferencesDialogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-references-dialog-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const string ProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

        </Project>
        """;

    private const string ProjectXmlWithReference = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\Library\Library.vbproj" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>A directory layout with two sibling projects, as a two-project solution has on disk.</summary>
    private (string MainProject, string OtherProject) CreateTwoProjects(string mainProjectXml)
    {
        var mainDirectory = Path.Combine(_root, "MainApp");
        var otherDirectory = Path.Combine(_root, "Library");
        Directory.CreateDirectory(mainDirectory);
        Directory.CreateDirectory(otherDirectory);

        var mainProject = Path.Combine(mainDirectory, "MainApp.vbproj");
        var otherProject = Path.Combine(otherDirectory, "Library.vbproj");
        File.WriteAllText(mainProject, mainProjectXml);
        File.WriteAllText(otherProject, ProjectXml);

        return (mainProject, otherProject);
    }

    [AvaloniaFact]
    public void TheOtherProjectStartsUncheckedWhenNotAlreadyReferenced()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);

        Assert.False(dialog.IsProjectChecked(otherProject));
    }

    [AvaloniaFact]
    public void AnAlreadyReferencedProjectStartsChecked()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXmlWithReference);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);

        Assert.True(dialog.IsProjectChecked(otherProject));
    }

    [AvaloniaFact]
    public void CheckingAnotherProjectAndClickingOkWritesTheProjectReference()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetProjectChecked(otherProject, true);
        Assert.True(dialog.IsProjectChecked(otherProject));

        dialog.OkButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        var written = File.ReadAllText(mainProject);
        Assert.Contains(@"<ProjectReference Include=""..\Library\Library.vbproj"" />", written);
    }

    [AvaloniaFact]
    public void CancellingAfterCheckingAnotherProjectLeavesTheFileUnchanged()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);
        var original = File.ReadAllText(mainProject);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetProjectChecked(otherProject, true);

        dialog.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        Assert.Equal(original, File.ReadAllText(mainProject));
        Assert.Null(dialog.AcceptedXml);
    }

    [AvaloniaFact]
    public void UncheckingAnAlreadyReferencedProjectAndClickingOkRemovesIt()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXmlWithReference);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetProjectChecked(otherProject, false);

        dialog.OkButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        Assert.DoesNotContain("ProjectReference", File.ReadAllText(mainProject));
    }

    [AvaloniaFact]
    public void OkCommitsThroughAnInjectedSinkInsteadOfTheFileWhenGiven()
    {
        // Stands in for an already-open editor tab: OK must hand the result
        // to this instead of writing the file directly.
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);
        string? sunk = null;

        var dialog = new ReferencesDialog(mainProject, [otherProject], text => sunk = text);
        dialog.SetProjectChecked(otherProject, true);

        dialog.OkButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        Assert.NotNull(sunk);
        Assert.Contains("ProjectReference", sunk);
        // The file on disk never got the write: the sink did instead.
        Assert.DoesNotContain("ProjectReference", File.ReadAllText(mainProject));
    }

    [AvaloniaFact]
    public void OkPreservesAUtf8ByteOrderMarkTheFileAlreadyHad()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);
        File.WriteAllText(mainProject, ProjectXml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetProjectChecked(otherProject, true);

        dialog.OkButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        var bytes = File.ReadAllBytes(mainProject);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("ProjectReference", File.ReadAllText(mainProject));
    }

    [AvaloniaFact]
    public void OkWritesNoByteOrderMarkWhenTheFileHadNone()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetProjectChecked(otherProject, true);

        dialog.OkButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        var bytes = File.ReadAllBytes(mainProject);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    // Central package management

    [AvaloniaFact]
    public void DetectsCentralPackageManagementFromADirectoryPackagesPropsAboveTheProject()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);
        File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);

        Assert.True(dialog.CentralPackageManagementForTests);
        Assert.True(dialog.PackageManagedCentrallyMessageVisibleForTests);
    }

    [AvaloniaFact]
    public void DoesNotDetectCentralPackageManagementWhenNoSignalIsPresent()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);

        Assert.False(dialog.CentralPackageManagementForTests);
        Assert.False(dialog.PackageManagedCentrallyMessageVisibleForTests);
    }

    [AvaloniaFact]
    public void AddingAPackageUnderCentralPackageManagementWritesNoVersionAndKeepsTheMessageShowing()
    {
        var (mainProject, otherProject) = CreateTwoProjects(ProjectXml);
        File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        var dialog = new ReferencesDialog(mainProject, [otherProject]);
        dialog.SetPackageFieldsForTests("Humanizer", "2.14.1");
        dialog.AddPackageFromFields();

        Assert.Contains(@"<PackageReference Include=""Humanizer"" />", dialog.WorkingXmlForTests);
        Assert.DoesNotContain("Version", dialog.WorkingXmlForTests);
        Assert.True(dialog.PackageManagedCentrallyMessageVisibleForTests);
    }

    [AvaloniaFact]
    public void UpdatingAnExistingPackageVersionUnderCentralPackageManagementDoesNotTouchTheFile()
    {
        const string withCentrallyManagedPackage = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """;
        var mainDirectory = Path.Combine(_root, "MainApp");
        Directory.CreateDirectory(mainDirectory);
        var mainProject = Path.Combine(mainDirectory, "MainApp.vbproj");
        File.WriteAllText(mainProject, withCentrallyManagedPackage);
        File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        var dialog = new ReferencesDialog(mainProject, []);
        dialog.SetPackageFieldsForTests("Newtonsoft.Json", "13.0.3");
        dialog.AddPackageFromFields();

        // No Version attribute was silently added or changed: updating a
        // centrally managed version is not a no-op anyone can see by its
        // absence — the message on screen is what tells them, not a quiet
        // XML edit that no longer matches Directory.Packages.props.
        Assert.Equal(withCentrallyManagedPackage, dialog.WorkingXmlForTests);
        Assert.True(dialog.PackageManagedCentrallyMessageVisibleForTests);
    }
}
