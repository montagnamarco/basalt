using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Completion asked about text newer than the workspace's copy.
///
/// While typing, the editor is always ahead: asking Roslyn about a position
/// that exists only in the newer text threw an IndexOutOfRangeException and
/// brought the whole application down.
/// </summary>
public sealed class CompletionRobustnessTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-completion", Guid.NewGuid().ToString("N"));

    private RoslynLanguageService? _service;

    public CompletionRobustnessTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageService Service, string File)> OpenAsync(string code)
    {
        var project = Path.Combine(_root, "Probe.vbproj");
        var source = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, code);

        _service = new RoslynLanguageService();
        await _service.OpenSolutionAsync(project);
        return (_service, source);
    }

    [Fact]
    public async Task HandlesAPositionBeyondTheWorkspaceCopy()
    {
        // The file on disk is short; the editor's text is longer.
        var (service, file) = await OpenAsync("Public Class P\nEnd Class");

        const string typed = """
            Public Class P
                Sub M()
                    Console.
                End Sub
            End Class
            """;

        var position = typed.IndexOf("Console.", StringComparison.Ordinal) + "Console.".Length;

        // Without passing the newer text this threw and killed the process.
        var completions = await service.GetCompletionsAsync(file, position, typed);

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.DisplayText == "WriteLine");
    }

    [Fact]
    public async Task ClampsAPositionPastTheEndOfTheText()
    {
        var (service, file) = await OpenAsync("Public Class P\nEnd Class");

        var completions = await service.GetCompletionsAsync(file, 10_000, "Public Class P\nEnd Class");

        Assert.NotNull(completions);
    }

    [Fact]
    public async Task StillWorksWithoutNewerText()
    {
        // The overload without text keeps working for callers that have none.
        var (service, file) = await OpenAsync("""
            Public Class P
                Sub M()
                    Dim s As String = ""
                End Sub
            End Class
            """);

        var completions = await service.GetCompletionsAsync(file, 20);

        Assert.NotNull(completions);
    }

    public void Dispose()
    {
        _service?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
