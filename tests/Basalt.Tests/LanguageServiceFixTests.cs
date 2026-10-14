using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Tests;

/// <summary>
/// Language-service defects found by probing the editor on a real solution:
/// signature help, hover, Extract Method and Rename.
/// </summary>
/// <remarks>
/// Each test states the symptom the probe saw. Run against real compilations
/// and workspaces, since every one of these depends on what Roslyn binds.
/// </remarks>
[Collection("Localizer culture")]
public sealed partial class LanguageServiceFixTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-langfix", Guid.NewGuid().ToString("N"));

    public LanguageServiceFixTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<RoslynLanguageService> OpenAsync(params (string Name, string Code)[] files)
    {
        var project = Path.Combine(_root, "Probe.vbproj");

        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        foreach (var (name, code) in files)
            await File.WriteAllTextAsync(Path.Combine(_root, name), code.ReplaceLineEndings("\n"));

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);

        return service;
    }

    private string PathOf(string name) => Path.Combine(_root, name);

    // ---- Signature help -----------------------------------------------------

    [Fact]
    public async Task SignatureHelpOffersEveryOverloadOfAnIncompleteCall()
    {
        // The probe typed "Foo(" against three overloads and saw one.
        const string code = """
            Public Class Probe
                Public Sub Foo()
                End Sub

                Public Sub Foo(a As Integer)
                End Sub

                Public Sub Foo(a As Integer, b As Integer)
                End Sub

                Public Sub Caller()
                    Foo(
                End Sub
            End Class
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var help = await service.GetSignatureHelpAsync(
            PathOf("Program.vb"), code.LastIndexOf("Foo(", StringComparison.Ordinal) + "Foo(".Length);

        Assert.NotNull(help);
        Assert.Equal(3, help!.Signatures.Count);
    }

    [Fact]
    public async Task SignatureHelpOffersEveryOverloadWhenTheCallAlreadyBinds()
    {
        // "Console.WriteLine()" binds to the parameterless overload, and the
        // probe saw "1 of 1" rather than the overloads of WriteLine.
        const string code = """
            Module Program
                Sub Main()
                    Console.WriteLine()
                End Sub
            End Module
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var help = await service.GetSignatureHelpAsync(
            PathOf("Program.vb"), code.IndexOf("WriteLine()", StringComparison.Ordinal) + "WriteLine(".Length);

        Assert.NotNull(help);
        Assert.True(help!.Signatures.Count > 1, $"Only {help.Signatures.Count} overload(s) offered.");
        Assert.All(help.Signatures, signature => Assert.Contains("WriteLine", signature.Signature, StringComparison.Ordinal));

        // The overload the call binds to is the one selected.
        Assert.Empty(help.Signatures[help.ActiveSignature].Parameters);
    }

    [Fact]
    public async Task SignatureHelpSelectsTheOverloadTheCallBindsTo()
    {
        const string code = """
            Public Class Probe
                Public Sub Send(value As String)
                End Sub

                Public Sub Send(value As String, times As Integer)
                End Sub

                Public Sub Caller()
                    Send("a", 2)
                End Sub
            End Class
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var help = await service.GetSignatureHelpAsync(
            PathOf("Program.vb"), code.IndexOf("2)", StringComparison.Ordinal));

        Assert.NotNull(help);
        Assert.Equal(2, help!.Signatures.Count);
        Assert.Equal(2, help.Signatures[help.ActiveSignature].Parameters.Count);
        Assert.Equal(1, help.ActiveParameter);
    }

    // ---- Hover --------------------------------------------------------------

    [Fact]
    public async Task QuickInfoSaysThatAVariableIsALocal()
    {
        const string code = """
            Module Program
                Sub Main()
                    Dim total As Integer = 1
                    Console.WriteLine(total)
                End Sub
            End Module
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var info = await service.GetQuickInfoAsync(
            PathOf("Program.vb"), code.IndexOf("total)", StringComparison.Ordinal) + 2);

        Assert.NotNull(info);
        Assert.StartsWith("(local variable) total As Integer", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuickInfoSaysThatAVariableIsAParameter()
    {
        const string code = """
            Module Program
                Sub Show(value As Integer)
                    Console.WriteLine(value)
                End Sub
            End Module
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var info = await service.GetQuickInfoAsync(
            PathOf("Program.vb"), code.IndexOf("value)", StringComparison.Ordinal) + 2);

        Assert.NotNull(info);
        Assert.StartsWith("(parameter) value As Integer", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuickInfoSaysThatAVariableIsAField()
    {
        const string code = """
            Public Class Counter
                Private _count As Integer

                Public Sub Bump()
                    _count += 1
                End Sub
            End Class
            """;

        using var service = await OpenAsync(("Counter.vb", code));

        var info = await service.GetQuickInfoAsync(
            PathOf("Counter.vb"), code.IndexOf("_count +=", StringComparison.Ordinal) + 2);

        Assert.NotNull(info);
        // Visual Studio names the containing type too: "(field) Counter._count As Integer".
        Assert.StartsWith("(field) Counter._count As Integer", info, StringComparison.Ordinal);
    }

    // ---- Extract Method -----------------------------------------------------

    private const string Sample = """
        Module Program
            Sub Main()
                Dim a = 3
                Dim b = 4
                Dim sum = a + b
                Console.WriteLine(sum)
            End Sub
        End Module
        """;

    [Fact]
    public async Task ExtractMethodKeepsTheCallAtTheIndentationOfItsFirstStatement()
    {
        // The selection starts at the first non-blank character, so the line's
        // indentation is already there; the call must not add it a second time.
        using var service = await OpenAsync(("Program.vb", Sample));

        var (start, length) = SelectionFrom(Sample, "Dim sum", "Console.WriteLine(sum)");
        var preview = await service.PreviewExtractMethodAsync(PathOf("Program.vb"), start, length, "PrintSum");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText.ReplaceLineEndings("\n");

        Assert.Contains("\n        PrintSum(a, b)\n", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("                PrintSum", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractMethodSeparatesTheNewMethodWithOneBlankLine()
    {
        using var service = await OpenAsync(("Program.vb", Sample));

        var (start, length) = SelectionFrom(Sample, "Dim sum", "Console.WriteLine(sum)");
        var preview = await service.PreviewExtractMethodAsync(PathOf("Program.vb"), start, length, "PrintSum");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText.ReplaceLineEndings("\n");

        // One blank line between the two methods, and none doubled at the end of the module.
        Assert.Contains("    End Sub\n\n    Private Sub PrintSum(", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", updated, StringComparison.Ordinal);
        Assert.Contains("    End Sub\nEnd Module", updated, StringComparison.Ordinal);
    }

    /// <summary>The span from the start of <paramref name="first"/> to the end of <paramref name="last"/>.</summary>
    private static (int Start, int Length) SelectionFrom(string source, string first, string last)
    {
        var start = source.IndexOf(first, StringComparison.Ordinal);
        var end = source.IndexOf(last, StringComparison.Ordinal) + last.Length;
        return (start, end - start);
    }

    // ---- Rename -------------------------------------------------------------

    [Fact]
    public async Task RenameRefusesANameThatIsAlreadyAMemberOfTheModule()
    {
        // Renaming Twice to Main gave the module two Main members, previewed
        // with no word of it. The renamer applies the collision; it is refused here.
        const string code = """
            Module Program
                Function Twice(x As Integer) As Integer
                    Return x * 2
                End Function

                Sub Main()
                    Dim v = Twice(3)
                End Sub
            End Module
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var preview = await service.PreviewRenameAsync(
            PathOf("Program.vb"), code.IndexOf("Twice(x", StringComparison.Ordinal) + 1, "Main");

        Assert.False(preview.CanApply);
        Assert.Contains("already has a member named Main", preview.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameRefusesALocalThatWouldDuplicateAnother()
    {
        // Nothing in the symbol table collides, so the compiler must report the
        // duplicate that the rename introduces, and that refuses it.
        const string code = """
            Module Program
                Sub Main()
                    Dim total As Integer = 1
                    Dim other As Integer = 2
                    Console.WriteLine(total + other)
                End Sub
            End Module
            """;

        using var service = await OpenAsync(("Program.vb", code));

        var preview = await service.PreviewRenameAsync(
            PathOf("Program.vb"), code.IndexOf("total As", StringComparison.Ordinal) + 1, "other");

        Assert.False(preview.CanApply);
        Assert.Contains("would leave an error", preview.Problem, StringComparison.Ordinal);
    }
}
