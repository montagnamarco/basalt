using Basalt.Workspace;
using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

/// <summary>
/// Changing a method's parameters, and the calls to it.
///
/// The declaration is the easy half. A signature changed without its calls
/// leaves code that does not compile, so what these mostly check is that the
/// calls came along — and that a call which cannot be rewritten with
/// certainty stops the change rather than leaving half of them updated.
/// </summary>
public sealed class ChangeSignatureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-signature", Guid.NewGuid().ToString("N"));

    public ChangeSignatureTests() => Directory.CreateDirectory(_root);

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

    private const string WithCalls = """
        Public Class Probe
            Public Function Add(first As Integer, second As Integer) As Integer
                Return first + second
            End Function

            Public Sub Use()
                Dim total = Add(1, 2)
                Dim other = Add(3, 4)
            End Sub
        End Class
        """;

    [Fact]
    public async Task ListsWhatTheMethodTakesNow()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var parameters = await service.GetParametersAsync(
            path, Caret(WithCalls, "Public Function Add"));

        Assert.Equal(2, parameters.Count);
        Assert.Equal("first", parameters[0].Name);
        Assert.Equal("Integer", parameters[0].Type);
        Assert.Equal("second", parameters[1].Name);
    }

    [Fact]
    public async Task ReordersTheParametersAndEveryCall()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([1, 0], []));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Add(second As Integer, first As Integer)", updated,
            StringComparison.Ordinal);

        // Both calls, or the ones left behind would pass the wrong values.
        Assert.Contains("Add(2, 1)", updated, StringComparison.Ordinal);
        Assert.Contains("Add(4, 3)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovesAParameterFromTheCallsToo()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([0], []));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Add(first As Integer)", updated, StringComparison.Ordinal);
        Assert.Contains("Add(1)", updated, StringComparison.Ordinal);
        Assert.Contains("Add(3)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddsAParameterAndPassesSomethingForIt()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([0, 1], [("third", "Integer", "0")]));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("third As Integer", updated, StringComparison.Ordinal);
        Assert.Contains("Add(1, 2, 0)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatesCallsInOtherFiles()
    {
        // A call in another file is exactly the one a hand edit would miss.
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var other = Path.Combine(_root, "Other.vb");

        await File.WriteAllTextAsync(other, """
            Public Class Elsewhere
                Public Sub M()
                    Dim p As New Probe()
                    Dim n = p.Add(9, 8)
                End Sub
            End Class
            """);

        // Reopened so the new file is part of the solution.
        service.Dispose();

        var reopened = new RoslynLanguageService();
        await reopened.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        using var _2 = reopened;

        var preview = await reopened.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([1, 0], []));

        Assert.True(preview.CanApply, preview.Problem);

        var changed = preview.Changes.SingleOrDefault(
            c => Path.GetFileName(c.FilePath) == "Other.vb");

        Assert.NotNull(changed);
        Assert.Contains("p.Add(8, 9)", changed.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesWhenACallUsesNamedArguments()
    {
        // Reordering would not mean the same thing for a named call.
        const string code = """
            Public Class Probe
                Public Sub M(first As Integer, second As Integer)
                End Sub

                Public Sub Use()
                    M(second:=2, first:=1)
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(code, "Public Sub M("),
            new SignatureChange([1, 0], []));

        Assert.False(preview.CanApply);
        Assert.Contains("named arguments", preview.Problem ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaysNothingChangedWhenTheOrderIsTheSame()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([0, 1], []));

        Assert.False(preview.CanApply);
        Assert.Contains("Nothing about the signature changed", preview.Problem ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoWhenTheCaretIsNotInAMethod()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Class Probe"),
            new SignatureChange([0], []));

        Assert.False(preview.CanApply);
        Assert.Contains("Put the caret in a Sub or a Function", preview.Problem ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAParameterThatDoesNotExist()
    {
        var (service, path) = await OpenAsync(WithCalls);
        using var _ = service;

        var preview = await service.PreviewChangeSignatureAsync(
            path, Caret(WithCalls, "Public Function Add"),
            new SignatureChange([0, 5], []));

        Assert.False(preview.CanApply);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
