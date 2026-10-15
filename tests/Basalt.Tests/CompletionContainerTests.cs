using Basalt.Core.Model;
using Basalt.Workspace;
using Basalt.Workspace.Completion;

namespace Basalt.Tests;

/// <summary>
/// What Roslyn really puts on a type entry, measured through the language
/// service in Microsoft.CodeAnalysis.Features 5.9.0.
/// </summary>
public sealed class CompletionContainerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-completion-container", Guid.NewGuid().ToString("N"));

    private RoslynLanguageService? _service;

    public CompletionContainerTests() => Directory.CreateDirectory(_root);

    private async Task<IReadOnlyList<CompletionItem>> CompleteTimAsync()
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

        const string code = """
            Imports System.Threading
            Imports System.Timers

            Public Class P
                Sub M()
                    Dim t = New Tim
                End Sub
            End Class
            """;

        await File.WriteAllTextAsync(source, code);

        _service = new RoslynLanguageService();
        await _service.OpenSolutionAsync(project);

        var position = code.IndexOf("New Tim", StringComparison.Ordinal) + "New Tim".Length;

        return await _service.GetCompletionsAsync(source, position, code);
    }

    /// <summary>
    /// Roslyn lists two imported types of one name as a single item, with no
    /// namespace on it. The one-row-per-name-and-container rule therefore has
    /// nothing to separate for imported types; if a Roslyn upgrade starts
    /// listing both, or records the namespace, this fails and the rule gets
    /// real input.
    /// </summary>
    [Fact]
    public async Task TwoImportedTypesOfOneNameAreOneRoslynItemWithNoContainer()
    {
        var completions = await CompleteTimAsync();

        var timers = completions.Where(item => item.DisplayText == "Timer").ToList();

        var timer = Assert.Single(timers);
        Assert.Null(timer.Container);
    }

    /// <summary>The matcher keeps both when a provider does record the containers.</summary>
    [Fact]
    public void TheMatcherKeepsTwoTimersWhoseContainersDiffer()
    {
        CompletionItem[] items =
        [
            new("Timer", "Timer", CompletionKind.Class) { Container = "System.Threading" },
            new("Timer", "Timer", CompletionKind.Class) { Container = "System.Timers" },
        ];

        Assert.Equal(2, CompletionMatcher.Filter(items, "Tim").Count);
    }

    public void Dispose()
    {
        _service?.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
