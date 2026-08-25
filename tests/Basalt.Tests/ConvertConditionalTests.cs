using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Turning an If chain into a Select Case, and back.
///
/// The refusals are the point: the two forms mean the same thing only when
/// every branch tests one value for equality, and rewriting a chain that does
/// something else would change what the program does.
/// </summary>
public sealed class ConvertConditionalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-convert", Guid.NewGuid().ToString("N"));

    public ConvertConditionalTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageService Service, string Path)> OpenAsync(string code)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var path = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(path, code);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        return (service, path);
    }

    private static int Caret(string code, string fragment)
    {
        var at = code.IndexOf(fragment, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{fragment}' is not in the source.");

        return at;
    }

    private const string Chain = """
        Public Class Probe
            Public Sub M(n As Integer)
                If n = 1 Then
                    Report("one")
                ElseIf n = 2 Then
                    Report("two")
                Else
                    Report("many")
                End If
            End Sub
            Private Sub Report(s As String)
            End Sub
        End Class
        """;

    [Fact]
    public async Task TurnsAnIfChainIntoASelectCase()
    {
        var (service, path) = await OpenAsync(Chain);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(Chain, "If n = 1"));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Select Case n", updated, StringComparison.Ordinal);
        Assert.Contains("Case 1", updated, StringComparison.Ordinal);
        Assert.Contains("Case 2", updated, StringComparison.Ordinal);
        Assert.Contains("Case Else", updated, StringComparison.Ordinal);
        Assert.Contains("End Select", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ElseIf", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsWhatEachBranchDid()
    {
        var (service, path) = await OpenAsync(Chain);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(Chain, "If n = 1"));

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Report(\"one\")", updated, StringComparison.Ordinal);
        Assert.Contains("Report(\"two\")", updated, StringComparison.Ordinal);
        Assert.Contains("Report(\"many\")", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAChainThatTestsDifferentThings()
    {
        // "If a = 1 ElseIf b = 2" is not one value against several, so a
        // Select Case would not mean the same.
        const string code = """
            Public Class Probe
                Public Sub M(a As Integer, b As Integer)
                    If a = 1 Then
                        M(1, 1)
                    ElseIf b = 2 Then
                        M(2, 2)
                    End If
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(path, Caret(code, "If a = 1"));

        Assert.False(preview.CanApply);
        Assert.Contains("not every branch", preview.Problem ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesAChainThatDoesNotCompareForEquality()
    {
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    If n > 1 Then
                        M(1)
                    ElseIf n > 2 Then
                        M(2)
                    End If
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(path, Caret(code, "If n > 1"));

        Assert.False(preview.CanApply);
    }

    [Fact]
    public async Task TurnsASelectCaseBackIntoAnIfChain()
    {
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    Select Case n
                        Case 1
                            M(1)
                        Case 2
                            M(2)
                        Case Else
                            M(0)
                    End Select
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(code, "Select Case n"));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("If n = 1 Then", updated, StringComparison.Ordinal);
        Assert.Contains("ElseIf n = 2 Then", updated, StringComparison.Ordinal);
        Assert.Contains("Else", updated, StringComparison.Ordinal);
        Assert.Contains("End If", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Select Case", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoinsSeveralValuesOfOneCaseWithOr()
    {
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    Select Case n
                        Case 1, 2
                            M(1)
                    End Select
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(code, "Select Case n"));

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("If n = 1 Or n = 2 Then", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesACaseThatTestsARange()
    {
        // "Case 1 To 5" is not an equality test, so an If chain would have to
        // say something else.
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    Select Case n
                        Case 1 To 5
                            M(1)
                    End Select
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(code, "Select Case n"));

        Assert.False(preview.CanApply);
        Assert.Contains("range", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesAnIfWithOnlyOneBranch()
    {
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    If n = 1 Then
                        M(1)
                    End If
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(path, Caret(code, "If n = 1"));

        Assert.False(preview.CanApply);
        Assert.Contains("two branches", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaysSoWhenTheCaretIsInNeither()
    {
        var (service, path) = await OpenAsync(Chain);
        using var _ = service;

        var preview = await service.PreviewConvertConditionalAsync(
            path, Caret(Chain, "Public Class"));

        Assert.False(preview.CanApply);
        Assert.Contains("Put the caret", preview.Problem ?? "", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
