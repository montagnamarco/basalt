using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// When typing a space opens the completion list, and which entry it
/// preselects: asked of Roslyn, as Visual Studio asks it.
/// </summary>
/// <remarks>
/// Visual Studio opens the list after "As ", "New ", "Of " and the like,
/// when there is something to offer there. After "= New " it preselects the declared type,
/// after "=" on an enum the enum's members. The editor used to open the list
/// on a letter or a dot only, so a type after As was typed from memory.
/// </remarks>
public sealed class CompletionTriggerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basalt-trigger", Guid.NewGuid().ToString("N"));

    private RoslynLanguageService? _service;

    public CompletionTriggerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _service?.Dispose();
        ScratchFolder.Delete(_root);
    }

    private async Task<(RoslynLanguageService Service, string File)> OpenAsync()
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
        await File.WriteAllTextAsync(source, "Public Class P\nEnd Class\n");

        _service = new RoslynLanguageService();
        await _service.OpenSolutionAsync(project);

        return (_service, source);
    }

    private static string InMethod(string statement) => $$"""
        Imports System.Collections.Generic

        Public Class P
            Implements System.IDisposable

            Sub M()
                {{statement}}
            End Sub

            Public Sub Dispose() Implements System.IDisposable.Dispose
            End Sub
        End Class
        """;

    [Theory]
    [InlineData("Dim x As ")]
    [InlineData("Dim items As New ")]
    [InlineData("Dim names As List(Of ")]
    public async Task ASpaceAfterTheseKeywordsOpensTheList(string typed)
    {
        var (service, file) = await OpenAsync();
        var code = InMethod(typed);
        var caret = code.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        Assert.True(await service.ShouldTriggerCompletionAsync(file, caret, code, ' '),
            $"a space after '{typed.Trim()}' did not open the list");
    }

    [Fact]
    public async Task ASpaceInsideAStringOffersNothing()
    {
        // Roslyn lets a space trigger almost anywhere in Visual Basic, as it
        // does for Visual Studio; what decides is whether any provider has
        // something to offer there, and inside a string none has.
        var (service, file) = await OpenAsync();
        var typed = "Console.WriteLine(\"a b ";
        var code = InMethod(typed + "\")");
        var caret = code.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        Assert.Empty(await service.GetCompletionsAsync(file, caret, code, typed: ' '));
    }

    [Fact]
    public async Task NewPreselectsTheDeclaredType()
    {
        var (service, file) = await OpenAsync();
        var typed = "Dim builder As System.Text.StringBuilder = New ";
        var code = InMethod(typed);
        var caret = code.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        var items = await service.GetCompletionsAsync(file, caret, code, typed: ' ');

        var preselected = Assert.Single(items, item => item.IsPreselected);

        // Named relative to what is imported: Text.StringBuilder here.
        Assert.EndsWith("StringBuilder", preselected.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverridesOffersTheOverridableMembersAndWritesTheWholeMember()
    {
        var (service, file) = await OpenAsync();
        var typed = "    Public Overrides ";
        var code = "Public Class Shape\n" +
                   "    Public Overridable Function Area(scale As Double) As Double\n" +
                   "        Return 0\n" +
                   "    End Function\n" +
                   "End Class\n\n" +
                   "Public Class Square\n" +
                   "    Inherits Shape\n\n" +
                   typed + "\n" +
                   "End Class\n";
        var caret = code.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        Assert.True(await service.ShouldTriggerCompletionAsync(file, caret, code, ' '));

        var items = await service.GetCompletionsAsync(file, caret, code, typed: ' ');
        var area = Assert.Single(items, item => item.DisplayText.StartsWith("Area", StringComparison.Ordinal));

        var commit = await area.ResolveCommit!(CancellationToken.None);

        Assert.NotNull(commit);
        var written = code[..commit.Start] + commit.Text + code[(commit.Start + commit.Length)..];

        Assert.Contains("Public Overrides Function Area(scale As Double) As Double", written);
        Assert.Contains("End Function\nEnd Class", written.Replace("\r\n", "\n"));
        Assert.Equal(1, written.Split("Public Overrides").Length - 1);
    }
}
