using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Extracting a constant, and putting a variable back where it was used.
///
/// Against a real project: whether a variable is written to later is decided
/// by what the names bind to, not by reading the text.
/// </summary>
public sealed class InlineAndConstantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-inline", Guid.NewGuid().ToString("N"));

    public InlineAndConstantTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Where a fragment sits in the source.</summary>
    private static (int Start, int Length) Find(string code, string fragment)
    {
        var start = code.IndexOf(fragment, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{fragment}' is not in the source.");

        return (start, fragment.Length);
    }

    // Extracting a constant

    [Fact]
    public async Task WritesAConstRatherThanADim()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim area = 3.14159 * 2
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, length) = Find(code, "3.14159");

        var preview = await service.PreviewExtractConstantAsync(path, start, length, "Pi");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Const Pi = 3.14159", updated, StringComparison.Ordinal);
        Assert.Contains("Pi * 2", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAConstantThatIsNotKnownAtCompileTime()
    {
        // A Const of something worked out at run time would not compile, so
        // it is refused with the part that stops it named.
        const string code = """
            Public Class Probe
                Public Sub M(n As Integer)
                    Dim doubled = n * 2
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, length) = Find(code, "n * 2");

        var preview = await service.PreviewExtractConstantAsync(path, start, length, "Twice");

        Assert.False(preview.CanApply);
        Assert.Contains("compile time", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AllowsAConstantMadeOnlyOfLiterals()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim seconds = 60 * 60
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, length) = Find(code, "60 * 60");

        var preview = await service.PreviewExtractConstantAsync(path, start, length, "AnHour");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Const AnHour = 60 * 60", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    // Inlining a variable

    [Fact]
    public async Task PutsAValueBackWhereItWasUsed()
    {
        const string code = """
            Public Class Probe
                Public Function M() As Integer
                    Dim ten = 10
                    Return ten
                End Function
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, _) = Find(code, "ten = 10");

        var preview = await service.PreviewInlineVariableAsync(path, start);

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Return 10", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Dim ten", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BracketsAValueThatCouldBeReadDifferentlyWhereItLands()
    {
        // "a + b" put into "x * 2" must not become "a + b * 2".
        const string code = """
            Public Class Probe
                Public Function M(a As Integer, b As Integer) As Integer
                    Dim sum = a + b
                    Return sum * 2
                End Function
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, _) = Find(code, "sum = a + b");

        var preview = await service.PreviewInlineVariableAsync(path, start);

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("(a + b) * 2", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAVariableThatIsGivenANewValue()
    {
        // Its uses would not all mean the same expression.
        const string code = """
            Public Class Probe
                Public Function M() As Integer
                    Dim count = 1
                    count = 2
                    Return count
                End Function
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, _) = Find(code, "count = 1");

        var preview = await service.PreviewInlineVariableAsync(path, start);

        Assert.False(preview.CanApply);
        Assert.Contains("new value", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesAVariableNothingUses()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim unused = 5
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, _) = Find(code, "unused = 5");

        var preview = await service.PreviewInlineVariableAsync(path, start);

        Assert.False(preview.CanApply);
        Assert.Contains("never used", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InlinesIntoEveryUse()
    {
        const string code = """
            Public Class Probe
                Public Function M() As Integer
                    Dim two = 2
                    Return two + two + two
                End Function
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var (start, _) = Find(code, "two = 2");

        var preview = await service.PreviewInlineVariableAsync(path, start);

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Return 2 + 2 + 2", updated, StringComparison.Ordinal);
        Assert.Contains("3 uses", preview.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoWhenTheCaretIsNotOnAVariable()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewInlineVariableAsync(path, code.IndexOf("Class", StringComparison.Ordinal));

        Assert.False(preview.CanApply);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
