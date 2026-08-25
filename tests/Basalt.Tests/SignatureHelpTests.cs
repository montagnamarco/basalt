using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The overloads offered while typing a call.
///
/// Run against real compilations rather than a stub: what makes this hard is
/// Roslyn's own view of the syntax, and a stub would only test the stub.
/// </summary>
public sealed class SignatureHelpTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-sighelp", Guid.NewGuid().ToString("N"));

    private string CreateProject(SourceLanguage language, string sourceCode)
    {
        Directory.CreateDirectory(_root);

        var isVb = language == SourceLanguage.VisualBasic;
        var projectPath = Path.Combine(_root, isVb ? "Probe.vbproj" : "Probe.csproj");
        var sourcePath = Path.Combine(_root, isVb ? "Program.vb" : "Program.cs");

        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(sourcePath, sourceCode);

        return sourcePath;
    }

    private async Task<RoslynLanguageService> OpenAsync(SourceLanguage language)
    {
        var service = new RoslynLanguageService();

        await service.OpenSolutionAsync(Path.Combine(
            _root, language == SourceLanguage.VisualBasic ? "Probe.vbproj" : "Probe.csproj"));

        return service;
    }

    [Fact]
    public async Task DescribesTheMethodBeingCalledInVisualBasic()
    {
        const string code = """
            Public Class Probe
                Public Sub Greet(name As String, times As Integer)
                End Sub

                Public Sub Caller()
                    Greet(
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        // The call, not the declaration: "Greet(" appears in both, and the
        // first occurrence is the Sub being defined.
        var help = await service.GetSignatureHelpAsync(
            path, code.LastIndexOf("Greet(", StringComparison.Ordinal) + "Greet(".Length);

        Assert.NotNull(help);
        Assert.Contains("Greet", help!.Signatures[0].Signature);
        Assert.Equal(2, help.Signatures[0].Parameters.Count);
    }

    [Fact]
    public async Task FollowsTheCaretFromOneArgumentToTheNext()
    {
        const string code = """
            Public Class Probe
                Public Sub Greet(name As String, times As Integer)
                End Sub

                Public Sub Caller()
                    Greet("a", 2)
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var call = code.IndexOf("Greet(\"a\"", StringComparison.Ordinal);

        var first = await service.GetSignatureHelpAsync(path, call + "Greet(".Length + 1);
        var second = await service.GetSignatureHelpAsync(path, call + "Greet(\"a\", ".Length);

        Assert.Equal(0, first!.ActiveParameter);
        Assert.Equal(1, second!.ActiveParameter);
    }

    [Fact]
    public async Task OffersEveryOverload()
    {
        const string code = """
            Public Class Probe
                Public Sub Send(value As String)
                End Sub

                Public Sub Send(value As Integer)
                End Sub

                Public Sub Caller()
                    Send(
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var help = await service.GetSignatureHelpAsync(
            path, code.IndexOf("Send(\r\n", StringComparison.Ordinal) is var i and >= 0
                ? i + "Send(".Length
                : code.LastIndexOf("Send(", StringComparison.Ordinal) + "Send(".Length);

        Assert.NotNull(help);
        Assert.Equal(2, help!.Signatures.Count);
    }

    [Fact]
    public async Task DescribesTheInnerCallWhenCallsAreNested()
    {
        // In "Outer(Inner(x))" the caret in x is describing Inner.
        const string code = """
            Public Class Probe
                Public Function Inner(a As Integer) As Integer
                    Return a
                End Function

                Public Sub Outer(b As Integer)
                End Sub

                Public Sub Caller()
                    Outer(Inner(1))
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var help = await service.GetSignatureHelpAsync(
            path, code.IndexOf("Inner(1", StringComparison.Ordinal) + "Inner(".Length);

        Assert.NotNull(help);
        Assert.Contains("Inner", help!.Signatures[0].Signature);
    }

    [Fact]
    public async Task SaysNothingOutsideACall()
    {
        const string code = """
            Public Class Probe
                Public Sub Caller()
                    Dim x As Integer = 1
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var help = await service.GetSignatureHelpAsync(
            path, code.IndexOf("Dim x", StringComparison.Ordinal) + 3);

        Assert.Null(help);
    }

    [Fact]
    public async Task CountsOnlyTheCommasOfTheCallItIsDescribing()
    {
        // The comma inside Inner belongs to Inner, not to Outer.
        const string code = """
            Public Class Probe
                Public Function Inner(a As Integer, b As Integer) As Integer
                    Return a
                End Function

                Public Sub Outer(x As Integer, y As Integer)
                End Sub

                Public Sub Caller()
                    Outer(Inner(1, 2), 3)
                End Sub
            End Class
            """;

        var path = CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        // Caret on the "3": the second argument of Outer, despite three commas
        // appearing before it in the text.
        var help = await service.GetSignatureHelpAsync(
            path, code.IndexOf("), 3)", StringComparison.Ordinal) + 3);

        Assert.NotNull(help);
        Assert.Contains("Outer", help!.Signatures[0].Signature);
        Assert.Equal(1, help.ActiveParameter);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
