using System.Text;
using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// A project reference <see cref="ProjectReferenceEditor"/> wrote is not just
/// text that looks right: "dotnet build" must actually see it and resolve a
/// type the referenced project declares.
/// </summary>
[Collection("PackageBuild")]
public sealed class ProjectReferenceBuildTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-project-reference-build-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task AProjectReferenceAddedByTheEditorBuildsAndResolvesTheOtherProjectsType()
    {
        var libraryDirectory = Path.Combine(_root, "Library");
        var appDirectory = Path.Combine(_root, "App");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(appDirectory);

        var libraryProject = Path.Combine(libraryDirectory, "Library.vbproj");
        var appProject = Path.Combine(appDirectory, "App.vbproj");

        await File.WriteAllTextAsync(libraryProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(libraryDirectory, "Greeter.vb"), """
            Public Class Greeter
                Public Shared Function Greet() As String
                    Return "Hello from the library"
                End Function
            End Class
            """);

        var appProjectXml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """;

        // The edit under test: a relative path computed the normal way (which
        // uses this OS's own separator), handed to the editor to normalise
        // and insert.
        var relativePath = Path.GetRelativePath(appDirectory, libraryProject);
        var editedXml = ProjectReferenceEditor.AddProjectReference(appProjectXml, relativePath);
        await File.WriteAllTextAsync(appProject, editedXml);

        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Program.vb"), """
            Module Program
                Sub Main()
                    System.Console.WriteLine(Greeter.Greet())
                End Sub
            End Module
            """);

        var builder = new MsBuildBuildService();
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };

        var result = await builder.BuildAsync(appProject);

        Assert.True(result.Succeeded, output.ToString());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
