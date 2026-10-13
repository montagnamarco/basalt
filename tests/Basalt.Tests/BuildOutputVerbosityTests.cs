using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// What a build writes to the Output panel: what Visual Studio shows, the
/// errors, the assembly produced and the outcome, not MSBuild's working notes.
/// </summary>
/// <remarks>
/// The build ran with "-v normal", and Ctrl+F5 on a one-file program filled the
/// Output panel with the compiler's full command line (hundreds of reference
/// paths) and the compiler server's pipe messages, burying "hello" under them.
/// Reintroduced once ("-v normal"): the first assertion failed.
/// </remarks>
public sealed class BuildOutputVerbosityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-verbosity-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string WriteProject(string source)
    {
        var project = Path.Combine(_root, "Quiet.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), source);
        return project;
    }

    [Fact]
    public async Task ASuccessfulBuildShowsTheAssemblyAndNotTheCompilersCommandLine()
    {
        var project = WriteProject("Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        var service = new MsBuildBuildService();
        var lines = new List<string>();
        service.OutputReceived += (_, line) => { lock (lines) lines.Add(line); };

        var result = await service.BuildAsync(project);

        Assert.True(result.Succeeded);
        string output;
        lock (lines) output = string.Join("\n", lines);
        Assert.DoesNotContain("BuildResponseFile", output, StringComparison.Ordinal);
        Assert.DoesNotContain("/reference:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("named pipe", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quiet -> ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedBuildStillReportsEachErrorOnItsLine()
    {
        var project = WriteProject("Module Program\n    Sub Main()\n        Dim x As Integer = y\n    End Sub\nEnd Module\n");

        var result = await new MsBuildBuildService().BuildAsync(project);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("BC30451", error.Id);
        Assert.Equal(3, error.Line);
    }
}
