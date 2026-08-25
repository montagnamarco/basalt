using Basalt.Extensibility;
using Basalt.QuickBasic;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The shared description model, checked against both languages.
///
/// The point of the abstraction: a tooltip draws what it is given without
/// knowing where it came from. Visual Basic fills it from Roslyn; QuickBASIC
/// has no compiler at all and fills the same shape.
/// </summary>
public class SymbolDescriptionModelTests
{
    [Fact]
    public void ASignatureCanBeReadAsPlainText()
    {
        var description = new SymbolDescription(
        [
            SymbolPart.Keyword("Function"),
            SymbolPart.Plain(" "),
            SymbolPart.Name("Greet"),
            SymbolPart.Plain("("),
            SymbolPart.Parameter("name"),
            SymbolPart.Plain(")")
        ]);

        Assert.Equal("Function Greet(name)", description.PlainSignature);
    }

    [Fact]
    public void APartKnowsWhatKindOfThingItIs()
    {
        // The parts carry meaning rather than appearance: the tooltip decides
        // that a parameter is bold, and decides it the same way for every
        // language.
        var description = new SymbolDescription([SymbolPart.Parameter("count")]);

        Assert.Equal(SymbolPartKind.ParameterName, description.Signature[0].Kind);
    }

    [Fact]
    public void NoActiveParameterMeansMinusOne()
    {
        // Hovering a name is not writing a call: nothing should be bold.
        var description = SymbolDescription.FromText("Dim x As Integer");

        Assert.Equal(-1, description.ActiveParameter);
    }

    [Fact]
    public void AParameterReadsAsNameAndType()
    {
        Assert.Equal("count As Integer",
            new ParameterDescription("count", "Integer").Display);
    }

    [Fact]
    public void AParameterWithoutATypeIsJustItsName()
    {
        Assert.Equal("count", new ParameterDescription("count").Display);
    }
}

/// <summary>
/// QuickBASIC filling the shared shape, with no compiler behind it.
/// </summary>
public class QuickBasicDescriptionTests
{
    private static readonly QuickBasicSymbolDescriptionProvider Provider = new();

    private static LanguageDocument Document(string text) => new("/p.bas", text);

    [Fact]
    public async Task ItDescribesABuiltInFunction()
    {
        var text = "PRINT MID$(a$, 2, 3)";

        var at = text.IndexOf("MID$", StringComparison.Ordinal) + 2;

        var description = await Provider.DescribeAsync(Document(text), at);

        Assert.NotNull(description);
        Assert.Contains("MID$", description.PlainSignature, StringComparison.Ordinal);
        Assert.Equal(3, description.Parameters.Count);
        Assert.Contains("middle", description.Documentation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EachParameterCarriesItsOwnExplanation()
    {
        var text = "PRINT MID$(a$, 2, 3)";

        var at = text.IndexOf("MID$", StringComparison.Ordinal) + 2;

        var description = await Provider.DescribeAsync(Document(text), at);

        Assert.NotNull(description);
        Assert.All(description.Parameters,
            p => Assert.False(string.IsNullOrWhiteSpace(p.Documentation)));
    }

    [Fact]
    public async Task WritingACallSaysWhichArgumentIsBeingTyped()
    {
        // Caret after the second comma: the third parameter.
        var text = "PRINT MID$(a$, 2, ";

        var set = await Provider.DescribeCallAsync(Document(text), text.Length);

        Assert.NotNull(set);

        var only = Assert.Single(set.Overloads);

        Assert.Equal(2, only.ActiveParameter);
    }

    [Fact]
    public async Task TheFirstArgumentIsNumberZero()
    {
        var text = "PRINT LEFT$(";

        var set = await Provider.DescribeCallAsync(Document(text), text.Length);

        Assert.NotNull(set);
        Assert.Equal(0, set.Overloads[0].ActiveParameter);
    }

    [Fact]
    public async Task ItDescribesAProcedureTheProgramDeclares()
    {
        var text = "SUB Greet (name AS STRING, times AS INTEGER)\nEND SUB\n\nGreet ";

        var at = text.LastIndexOf("Greet", StringComparison.Ordinal) + 2;

        var description = await Provider.DescribeAsync(Document(text), at);

        Assert.NotNull(description);
        Assert.Contains("SUB", description.PlainSignature, StringComparison.Ordinal);
        Assert.Equal(2, description.Parameters.Count);
        Assert.Equal("name", description.Parameters[0].Name);
        Assert.Equal("STRING", description.Parameters[0].Type);
    }

    [Fact]
    public async Task TheParameterNamesAreMarkedAsSuch()
    {
        // What lets the tooltip put the current one in bold.
        var text = "PRINT LEN(a$)";

        var at = text.IndexOf("LEN", StringComparison.Ordinal) + 1;

        var description = await Provider.DescribeAsync(Document(text), at);

        Assert.NotNull(description);
        Assert.Contains(description.Signature, p => p.Kind == SymbolPartKind.ParameterName);
        Assert.Contains(description.Signature, p => p.Kind == SymbolPartKind.Type);
    }

    [Fact]
    public async Task AnUnknownNameIsNotDescribed()
    {
        var text = "PRINT Nonesuch(1)";

        var at = text.IndexOf("Nonesuch", StringComparison.Ordinal) + 2;

        Assert.Null(await Provider.DescribeAsync(Document(text), at));
    }

    [Fact]
    public async Task APositionOutsideACallDescribesNoCall()
    {
        var text = "PRINT 1\n";

        Assert.Null(await Provider.DescribeCallAsync(Document(text), text.Length));
    }

    [Fact]
    public async Task AHalfWrittenProgramStillAnswersForItsIntrinsics()
    {
        // The parser fails on this; the intrinsics do not depend on it.
        var text = "SUB Broken (\nPRINT LEN(";

        var set = await Provider.DescribeCallAsync(Document(text), text.Length);

        Assert.NotNull(set);
        Assert.Contains("LEN", set.Overloads[0].PlainSignature, StringComparison.Ordinal);
    }
}
