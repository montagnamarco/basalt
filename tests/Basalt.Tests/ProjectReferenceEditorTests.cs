using System.Xml.Linq;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Project &gt; References… edits a project file by surgical text editing
/// rather than by parsing it into an XML tree and writing the whole thing
/// back out: every line that is not the one changed must survive exactly,
/// comments included, so these tests compare whole files rather than only
/// the reference lists each edit produces.
/// </summary>
public sealed class ProjectReferenceEditorTests
{
    private const string SampleProject = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <!-- Empty, or VB prefixes it to every type. -->
            <RootNamespace></RootNamespace>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

        </Project>
        """;

    private const string SampleProjectWithGroups = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\Other\Other.vbproj" />
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
          </ItemGroup>

        </Project>
        """;

    // Projects

    [Fact]
    public void AddsAProjectReferenceIntoANewItemGroupWhenNoneExists()
    {
        var result = ProjectReferenceEditor.AddProjectReference(SampleProject, @"..\Library\Library.vbproj");

        Assert.Contains(@"<ProjectReference Include=""..\Library\Library.vbproj"" />", result);
        Assert.Contains(@"..\Library\Library.vbproj", ProjectReferenceEditor.GetProjectReferences(result));
    }

    [Fact]
    public void AddsAProjectReferenceNextToAnExistingOne()
    {
        var result = ProjectReferenceEditor.AddProjectReference(SampleProjectWithGroups, @"..\Second\Second.vbproj");

        var references = ProjectReferenceEditor.GetProjectReferences(result);
        Assert.Equal(2, references.Count);
        Assert.Contains(@"..\Other\Other.vbproj", references);
        Assert.Contains(@"..\Second\Second.vbproj", references);

        // The new item joined the existing ProjectReference group rather
        // than getting a second ItemGroup of its own.
        Assert.Equal(
            CountOccurrences(SampleProjectWithGroups, "<ItemGroup>"),
            CountOccurrences(result, "<ItemGroup>"));
    }

    [Fact]
    public void NormalisesAForwardSlashPathToBackslashesLikeVisualStudio()
    {
        var result = ProjectReferenceEditor.AddProjectReference(SampleProject, "../Library/Library.vbproj");

        Assert.Contains(@"..\Library\Library.vbproj", ProjectReferenceEditor.GetProjectReferences(result));
        Assert.DoesNotContain("../Library/Library.vbproj", result);
    }

    [Fact]
    public void AddingTheSameProjectReferenceTwiceIsIdempotent()
    {
        var once = ProjectReferenceEditor.AddProjectReference(SampleProject, @"..\Library\Library.vbproj");
        var twice = ProjectReferenceEditor.AddProjectReference(once, "../Library/Library.vbproj");

        Assert.Equal(once, twice);
    }

    [Fact]
    public void RemovesAProjectReference()
    {
        var result = ProjectReferenceEditor.RemoveProjectReference(SampleProjectWithGroups, @"..\Other\Other.vbproj");

        Assert.Empty(ProjectReferenceEditor.GetProjectReferences(result));
        Assert.DoesNotContain("ProjectReference", result);
    }

    [Fact]
    public void RemovingAProjectReferenceThatIsNotThereLeavesTheFileUnchanged()
    {
        var result = ProjectReferenceEditor.RemoveProjectReference(SampleProjectWithGroups, @"..\Nope\Nope.vbproj");

        Assert.Equal(SampleProjectWithGroups, result);
    }

    // Assemblies

    [Fact]
    public void AddsAnAssemblyReferenceWithAHintPath()
    {
        var result = ProjectReferenceEditor.AddAssemblyReference(SampleProject, "Acme.Widgets", @"..\libs\Acme.Widgets.dll");

        var reference = Assert.Single(ProjectReferenceEditor.GetAssemblyReferences(result));
        Assert.Equal("Acme.Widgets", reference.Include);
        Assert.Equal(@"..\libs\Acme.Widgets.dll", reference.HintPath);
        Assert.Contains("<HintPath>..\\libs\\Acme.Widgets.dll</HintPath>", result);
    }

    [Fact]
    public void AddsABareAssemblyReferenceWithoutAHintPath()
    {
        var result = ProjectReferenceEditor.AddAssemblyReference(SampleProject, "System.Data", null);

        var reference = Assert.Single(ProjectReferenceEditor.GetAssemblyReferences(result));
        Assert.Equal("System.Data", reference.Include);
        Assert.Null(reference.HintPath);
        Assert.Contains(@"<Reference Include=""System.Data"" />", result);
    }

    [Fact]
    public void RemovesAnAssemblyReference()
    {
        var added = ProjectReferenceEditor.AddAssemblyReference(SampleProject, "Acme.Widgets", @"..\libs\Acme.Widgets.dll");
        var removed = ProjectReferenceEditor.RemoveAssemblyReference(added, "Acme.Widgets");

        Assert.Empty(ProjectReferenceEditor.GetAssemblyReferences(removed));
    }

    // Packages

    [Fact]
    public void ListsThePackagesAProjectAlreadyReferences()
    {
        var packages = ProjectReferenceEditor.GetPackageReferences(SampleProjectWithGroups);

        var package = Assert.Single(packages);
        Assert.Equal("Newtonsoft.Json", package.Id);
        Assert.Equal("13.0.1", package.Version);
    }

    [Fact]
    public void AddsAPackageReferenceByIdAndVersion()
    {
        var result = ProjectReferenceEditor.AddPackageReference(SampleProject, "Humanizer", "2.14.1");

        var package = Assert.Single(ProjectReferenceEditor.GetPackageReferences(result));
        Assert.Equal("Humanizer", package.Id);
        Assert.Equal("2.14.1", package.Version);
    }

    [Fact]
    public void AddingAnExistingPackageUpdatesItsVersionInPlace()
    {
        var result = ProjectReferenceEditor.AddPackageReference(SampleProjectWithGroups, "Newtonsoft.Json", "13.0.3");

        var packages = ProjectReferenceEditor.GetPackageReferences(result);
        var package = Assert.Single(packages);
        Assert.Equal("13.0.3", package.Version);

        // Nothing else in the file moved: still the same number of lines.
        Assert.Equal(
            SampleProjectWithGroups.Split('\n').Length,
            result.Split('\n').Length);
    }

    [Fact]
    public void RemovesAPackageReferenceByIdIgnoringCase()
    {
        var result = ProjectReferenceEditor.RemovePackageReference(SampleProjectWithGroups, "newtonsoft.json");

        Assert.Empty(ProjectReferenceEditor.GetPackageReferences(result));
    }

    // Formatting

    [Fact]
    public void PreservesCommentsAndBlankLinesEverywhereElse()
    {
        var result = ProjectReferenceEditor.AddProjectReference(SampleProject, @"..\Library\Library.vbproj");

        Assert.Contains("<!-- Empty, or VB prefixes it to every type. -->", result);
        Assert.Contains("<RootNamespace></RootNamespace>", result);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", result);
    }

    [Fact]
    public void PreservesCarriageReturnLineFeedEndingsWhenTheFileAlreadyUsesThem()
    {
        var withCrLf = SampleProject.Replace("\n", "\r\n");

        var result = ProjectReferenceEditor.AddProjectReference(withCrLf, @"..\Library\Library.vbproj");

        Assert.Contains("\r\n", result);
        Assert.DoesNotContain("\r\r\n", result);
        // Every line ending in the result is CRLF, matching the source file's own style.
        var withoutCrLf = result.Replace("\r\n", "");
        Assert.DoesNotContain("\n", withoutCrLf);
        Assert.DoesNotContain("\r", withoutCrLf);
    }

    // Escaping

    [Fact]
    public void AnAmpersandInAProjectReferencePathIsEscapedAndRoundTrips()
    {
        var result = ProjectReferenceEditor.AddProjectReference(SampleProject, @"..\R&D\Lib\Lib.vbproj");

        // Written escaped, so the file stays valid XML...
        Assert.Contains("&amp;D", result);
        Assert.DoesNotContain("R&D", result);
        XDocument.Parse(result);

        // ...and read back unescaped, exactly the path that went in.
        Assert.Contains(@"..\R&D\Lib\Lib.vbproj", ProjectReferenceEditor.GetProjectReferences(result));
    }

    [Fact]
    public void QuotesAndAnglesInAHintPathAreEscapedAndRoundTrip()
    {
        var result = ProjectReferenceEditor.AddAssemblyReference(
            SampleProject, "Acme.Widgets", @"..\lib's <special>\Acme.Widgets.dll");

        XDocument.Parse(result);

        var reference = Assert.Single(ProjectReferenceEditor.GetAssemblyReferences(result));
        Assert.Equal(@"..\lib's <special>\Acme.Widgets.dll", reference.HintPath);
    }

    // Update= items

    private const string SampleProjectWithUpdateItem = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Update="..\Other\Other.vbproj">
              <Private>false</Private>
            </ProjectReference>
          </ItemGroup>

        </Project>
        """;

    [Fact]
    public void AnItemDeclaredWithUpdateIsVisibleToGetProjectReferences()
    {
        var references = ProjectReferenceEditor.GetProjectReferences(SampleProjectWithUpdateItem);

        Assert.Contains(@"..\Other\Other.vbproj", references);
    }

    [Fact]
    public void AddingAProjectReferenceAlreadyDeclaredWithUpdateIsANoOp()
    {
        var result = ProjectReferenceEditor.AddProjectReference(
            SampleProjectWithUpdateItem, @"..\Other\Other.vbproj");

        Assert.Equal(SampleProjectWithUpdateItem, result);
        // Never a second, conflicting Include item for the same reference.
        Assert.DoesNotContain("Include=\"..\\Other\\Other.vbproj\"", result);
    }

    [Fact]
    public void RemovingAProjectReferenceDeclaredOnlyWithUpdateRemovesThatItem()
    {
        var result = ProjectReferenceEditor.RemoveProjectReference(
            SampleProjectWithUpdateItem, @"..\Other\Other.vbproj");

        Assert.Empty(ProjectReferenceEditor.GetProjectReferences(result));
        Assert.DoesNotContain("ProjectReference", result);
    }

    [Fact]
    public void RemovingAReferenceWithBothAnIncludeAndAnUpdateItemRemovesTheIncludeOne()
    {
        const string both = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Other\Other.vbproj" />
                <ProjectReference Update="..\Other\Other.vbproj">
                  <Private>false</Private>
                </ProjectReference>
              </ItemGroup>
            </Project>
            """;

        var result = ProjectReferenceEditor.RemoveProjectReference(both, @"..\Other\Other.vbproj");

        Assert.DoesNotContain("Include=\"..\\Other\\Other.vbproj\"", result);
        // The metadata-only Update item is untouched.
        Assert.Contains("Update=\"..\\Other\\Other.vbproj\"", result);
        Assert.Contains("<Private>false</Private>", result);
    }

    // Case-insensitive matching

    [Fact]
    public void AddingAProjectReferenceThatDiffersOnlyByCaseIsANoOp()
    {
        var once = ProjectReferenceEditor.AddProjectReference(SampleProject, @"..\Library\Library.vbproj");
        var twice = ProjectReferenceEditor.AddProjectReference(once, @"..\LIBRARY\library.vbproj");

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AddingAnAssemblyReferenceThatDiffersOnlyByCaseIsANoOp()
    {
        var once = ProjectReferenceEditor.AddAssemblyReference(SampleProject, "Acme.Widgets", null);
        var twice = ProjectReferenceEditor.AddAssemblyReference(once, "ACME.WIDGETS", null);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void RemovesAnAssemblyReferenceIgnoringCase()
    {
        var added = ProjectReferenceEditor.AddAssemblyReference(SampleProject, "Acme.Widgets", null);
        var removed = ProjectReferenceEditor.RemoveAssemblyReference(added, "ACME.WIDGETS");

        Assert.Empty(ProjectReferenceEditor.GetAssemblyReferences(removed));
    }

    // Central package management

    [Fact]
    public void DetectsCentralPackageManagementDeclaredInTheProjectItself()
    {
        const string withCpm = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """;

        Assert.True(ProjectReferenceEditor.DeclaresCentralPackageManagement(withCpm));
        Assert.False(ProjectReferenceEditor.DeclaresCentralPackageManagement(SampleProject));
    }

    [Fact]
    public void AddingAPackageUnderCentralPackageManagementWritesNoVersion()
    {
        var result = ProjectReferenceEditor.AddPackageReference(
            SampleProject, "Humanizer", "2.14.1", centralPackageManagement: true);

        Assert.Contains(@"<PackageReference Include=""Humanizer"" />", result);
        Assert.DoesNotContain("Version", result);

        var package = Assert.Single(ProjectReferenceEditor.GetPackageReferences(result));
        Assert.Equal("", package.Version);
    }

    [Fact]
    public void UpdatingAnExistingPackageUnderCentralPackageManagementDoesNothingToTheFile()
    {
        const string withCentrallyManagedPackage = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """;

        var result = ProjectReferenceEditor.AddPackageReference(
            withCentrallyManagedPackage, "Newtonsoft.Json", "13.0.3", centralPackageManagement: true);

        // No Version attribute appears out of nowhere: the file is byte-for-byte unchanged.
        Assert.Equal(withCentrallyManagedPackage, result);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
