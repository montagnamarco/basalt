using Basalt.Extensibility;
using Basalt.Workspace;
using Basalt.Workspace.Languages;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic filling the same shape QuickBASIC fills.
///
/// Built from Roslyn's display parts rather than from a formatted string: a
/// tooltip has to know which run is the parameter being written, and a string
/// cannot say.
/// </summary>
public sealed class VbDescriptionTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-vbdesc-").FullName;

    private RoslynLanguageService _service = null!;
    private ISymbolDescriptionProvider _provider = null!;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private const string Code =
        "Public Class Greeter\n"
      + "    ''' <summary>Says hello to someone.</summary>\n"
      + "    ''' <param name=\"name\">Who to greet.</param>\n"
      + "    ''' <param name=\"times\">How many times.</param>\n"
      + "    Public Function Greet(name As String, times As Integer) As String\n"
      + "        Return name\n"
      + "    End Function\n"
      + "End Class\n";

    public async ValueTask InitializeAsync()
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

        await File.WriteAllTextAsync(ProgramPath, Code);

        _service = new RoslynLanguageService();

        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        var provider = RoslynLanguageProvider.CreateVisualBasic(
            _service, new RoslynFormattingService());

        _provider = provider.Descriptions!;
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    private LanguageDocument Document(string? text = null) =>
        new(ProgramPath, text ?? Code);

    [Fact]
    public void TheLanguageOffersADescriber()
    {
        Assert.NotNull(_provider);
    }

    [Fact]
    public async Task ItDescribesAMethod()
    {
        var at = Code.IndexOf("Greet(", StringComparison.Ordinal) + 2;

        var description = await _provider.DescribeAsync(Document(), at);

        Assert.NotNull(description);
        Assert.Contains("Greet", description.PlainSignature, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItBringsTheDocumentationComment()
    {
        var at = Code.IndexOf("Greet(", StringComparison.Ordinal) + 2;

        var description = await _provider.DescribeAsync(Document(), at);

        Assert.NotNull(description);
        Assert.Contains("hello", description.Documentation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EachParameterCarriesItsOwnExplanation()
    {
        var at = Code.IndexOf("Greet(", StringComparison.Ordinal) + 2;

        var description = await _provider.DescribeAsync(Document(), at);

        Assert.NotNull(description);
        Assert.Equal(2, description.Parameters.Count);
        Assert.Contains("greet", description.Parameters[0].Documentation!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheParameterNamesAreMarkedAsSuch()
    {
        // What lets the tooltip put the current one in bold.
        var at = Code.IndexOf("Greet(", StringComparison.Ordinal) + 2;

        var description = await _provider.DescribeAsync(Document(), at);

        Assert.NotNull(description);
        Assert.Contains(description.Signature, p => p.Kind == SymbolPartKind.ParameterName);
    }

    [Fact]
    public async Task WritingACallSaysWhichArgumentIsBeingTyped()
    {
        var caller = Code
            + "Public Class Other\n"
            + "    Sub M()\n"
            + "        Dim g As New Greeter()\n"
            + "        g.Greet(\"hi\", \n"
            + "    End Sub\n"
            + "End Class\n";

        var at = caller.IndexOf("g.Greet(\"hi\", ", StringComparison.Ordinal)
               + "g.Greet(\"hi\", ".Length;

        var set = await _provider.DescribeCallAsync(Document(caller), at);

        Assert.NotNull(set);
        Assert.NotEmpty(set.Overloads);

        // After the first comma: the second parameter.
        Assert.Equal(1, set.Overloads[0].ActiveParameter);
    }

    [Fact]
    public async Task APositionOutsideACallDescribesNoCall()
    {
        Assert.Null(await _provider.DescribeCallAsync(Document(), 0));
    }
}
