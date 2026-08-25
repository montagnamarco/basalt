using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The table that ties generated Visual Basic back to its template.
///
/// It is what lets an error, a caret or a breakpoint travel between the two
/// documents. Everything else in the Razor tooling leans on it, so it is
/// tested on its own before anything is built on top.
/// </summary>
public sealed class SourceMapTests
{
    /// <summary>
    /// Two mappings with a gap between them.
    ///
    /// The gap matters: it is the generated code the template did not write,
    /// which is exactly what the behaviours differ about.
    /// </summary>
    private static SourceMap TwoMappings() =>
        new(
        [
            new SourceMapping(new SourceSpan(10, 5), new SourceSpan(100, 5), 1, 10),
            new SourceMapping(new SourceSpan(30, 8), new SourceSpan(200, 8), 3, 20)
        ]);

    [Fact]
    public void MapsAPositionInsideAMappingBothWays()
    {
        var map = TwoMappings();

        Assert.Equal(100, map.ToGenerated(10));
        Assert.Equal(10, map.ToOriginal(100));
    }

    [Fact]
    public void KeepsTheOffsetWithinTheMapping()
    {
        // A caret halfway through an expression lands halfway through it on
        // the other side, not at its start.
        var map = TwoMappings();

        Assert.Equal(102, map.ToGenerated(12));
        Assert.Equal(12, map.ToOriginal(102));
    }

    [Fact]
    public void MapsTheSecondMappingToo()
    {
        var map = TwoMappings();

        Assert.Equal(200, map.ToGenerated(30));
        Assert.Equal(30, map.ToOriginal(200));
    }

    [Fact]
    public void SaysNothingAboutCodeTheTemplateDidNotWrite()
    {
        // The class header and the closing statements belong to nobody, and
        // an answer would put an error on a line the user never typed.
        var map = TwoMappings();

        Assert.Null(map.ToOriginal(50));
        Assert.Null(map.ToOriginal(150));
    }

    [Fact]
    public void StrictRefusesAPositionJustPastTheEnd()
    {
        var map = TwoMappings();

        Assert.Null(map.ToGenerated(15, MappingBehavior.Strict));
    }

    [Fact]
    public void InclusiveTakesAPositionJustPastTheEnd()
    {
        // Which is where the caret sits when something has just been typed.
        var map = TwoMappings();

        Assert.Equal(105, map.ToGenerated(15, MappingBehavior.Inclusive));
    }

    [Fact]
    public void InferredLooksForwardToTheNextMapping()
    {
        var map = TwoMappings();

        // Position 20 is in the gap; the next mapping starts at 30.
        Assert.Equal(200, map.ToGenerated(20, MappingBehavior.Inferred));
    }

    [Fact]
    public void InferredNeverLooksBackwards()
    {
        // An error in generated code belongs to what comes after it, not
        // before: Razor made the same choice, and getting it wrong puts
        // errors on the wrong line.
        var map = TwoMappings();

        Assert.Null(map.ToGenerated(100, MappingBehavior.Inferred));
    }

    [Fact]
    public void MapsALineToItsTemplateLine()
    {
        var map = TwoMappings();

        Assert.Equal(1, map.OriginalLineOf(10));
        Assert.Equal(3, map.OriginalLineOf(20));
        Assert.Null(map.OriginalLineOf(15));
    }

    [Fact]
    public void SaysNothingWhenThereAreNoMappingsAtAll()
    {
        Assert.Null(SourceMap.Empty.ToOriginal(0));
        Assert.Null(SourceMap.Empty.ToGenerated(0));
        Assert.Empty(SourceMap.Empty.Mappings);
    }

    [Fact]
    public void KeepsTheMappingsInTemplateOrder()
    {
        // Given out of order on purpose: the table sorts them itself, since
        // the lookups are binary searches.
        var map = new SourceMap(
        [
            new SourceMapping(new SourceSpan(30, 8), new SourceSpan(200, 8), 3, 20),
            new SourceMapping(new SourceSpan(10, 5), new SourceSpan(100, 5), 1, 10)
        ]);

        Assert.Equal(10, map.Mappings[0].Original.Start);
        Assert.Equal(30, map.Mappings[1].Original.Start);
    }

    [Fact]
    public void FindsTheRightMappingAmongMany()
    {
        // The lookup is a binary search, so a wrong bound shows up only with
        // enough entries to have a middle.
        var map = new SourceMap(
            Enumerable.Range(0, 50)
                .Select(i => new SourceMapping(
                    new SourceSpan(i * 10, 5), new SourceSpan(1000 + i * 10, 5), i, 100 + i)));

        Assert.Equal(1000, map.ToGenerated(0));
        Assert.Equal(1250, map.ToGenerated(250));
        Assert.Equal(1490, map.ToGenerated(490));

        // And the holes between them are still holes.
        Assert.Null(map.ToGenerated(255));
    }
}
