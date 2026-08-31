using System.Diagnostics;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Whether the language service hands work back before it has done it.
/// </summary>
/// <remarks>
/// Roslyn's async methods return a completed task whenever the answer is
/// already known, so awaiting one does not yield and everything after it runs
/// on the caller. In the IDE the caller is the interface thread, and a method
/// that looks asynchronous while doing 60ms of work on it is a stutter with
/// nothing in the code to explain it.
///
/// These measure the gap between calling and being given the task back. It is
/// a timing test, so the thresholds are generous: they are there to catch the
/// difference between "returned at once" and "did all the work first", not to
/// police milliseconds.
/// </remarks>
public sealed class LanguageServiceThreadingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-threading", Guid.NewGuid().ToString("N"));

    public LanguageServiceThreadingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>How long the caller may be held before it counts as blocking.</summary>
    private const double Budget = 10;

    /// <summary>A project with a file big enough to cost something to analyse.</summary>
    private async Task<(RoslynLanguageService Service, string File)> BigProjectAsync()
    {
        File.WriteAllText(Path.Combine(_root, "P.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var body = string.Join("\n", Enumerable.Range(1, 300).Select(i =>
            $"    Public Function F{i}(x As Integer) As Integer\n        Return x + {i}\n    End Function"));

        var file = Path.Combine(_root, "Big.vb");
        File.WriteAllText(file, $"Module Big\n{body}\nEnd Module\n");

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "P.vbproj"));

        // Warm, so the first call's compilation is not what is measured.
        await service.GetDiagnosticsAsync(file);

        return (service, file);
    }

    [Fact]
    public async Task DiagnosticsDoNotHoldTheCaller()
    {
        // After a typing pause, which is when they are asked for and when
        // nothing is cached.
        var (service, file) = await BigProjectAsync();

        await service.UpdateDocumentAsync(file, File.ReadAllText(file) + "\n' edit\n");

        var watch = Stopwatch.StartNew();
        var task = service.GetDiagnosticsAsync(file);
        var held = watch.Elapsed.TotalMilliseconds;

        await task;

        Assert.True(held < Budget,
            $"the caller was held for {held:0.0}ms; the work belongs on another thread");
    }

    [Fact]
    public async Task CompletionsDoNotHoldTheCaller()
    {
        // On every keystroke after a dot, which is precisely when the editor
        // must not stutter.
        var (service, file) = await BigProjectAsync();

        await service.UpdateDocumentAsync(file, File.ReadAllText(file) + "\n' edit\n");

        var watch = Stopwatch.StartNew();
        var task = service.GetCompletionsAsync(file, 20);
        var held = watch.Elapsed.TotalMilliseconds;

        await task;

        Assert.True(held < Budget,
            $"the caller was held for {held:0.0}ms; the work belongs on another thread");
    }

    [Fact]
    public async Task StillAnswersWithTheDiagnosticsItFound()
    {
        // Moving work off the thread must not lose it.
        var (service, file) = await BigProjectAsync();

        await service.UpdateDocumentAsync(file, "Module Big\n    Dim x As Integer = \nEnd Module\n");

        Assert.NotEmpty(await service.GetDiagnosticsAsync(file));
    }
}
