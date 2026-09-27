using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The directives an editor offers, checked against the parser that reads
/// them: one opinion about the language, not two.
/// </summary>
public sealed class DirectiveTableTests
{
    public static TheoryData<string> Names() => [.. VbHtmlDirectives.All.Select(directive => directive.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void EachDirectiveOfferedIsOneTheParserReads(string name)
    {
        var directive = VbHtmlDirectives.All.Single(entry => entry.Name == name);
        var path = (directive.Target & DirectiveTarget.View) != 0 ? "Probe.vbhtml" : "Probe.vbrazor";

        var parsed = VbHtmlParser.Parse(directive.Sample + "\n");

        Assert.Empty(parsed.Diagnostics.Where(diagnostic => diagnostic.AppliesTo(path)));

        var first = Assert.Single(parsed.Nodes, node => node is not HtmlNode);

        var read = first switch
        {
            DirectiveNode node => node.Name,
            StatementNode => "Code",
            FunctionsNode => "Functions",
            SectionNode => "Section",
            BlockNode block when block.Opening.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) => name,
            BlockNode block => block.Opening,
            _ => first.GetType().Name
        };

        Assert.Equal(name, read, ignoreCase: true);
    }

    [Theory]
    [InlineData("@Mo", 3, true)]
    [InlineData("    @", 5, true)]
    [InlineData("<p>@Mo", 6, false)]
    [InlineData("@Model.Na", 9, false)]
    [InlineData("a@b", 2, false)]
    public void ADirectiveIsOfferedOnlyWhereOneIsWritten(string text, int position, bool expected)
    {
        Assert.Equal(expected, VbHtmlDirectives.IsDirectivePosition(text, position));
    }

    [Theory]
    [InlineData("Index.vbhtml", "ModelType", "rendermode")]
    [InlineData("Counter.vbrazor", "rendermode", "ModelType")]
    public async Task TheEditorsOfferTheDirectivesAtTheStartOfALine(string file, string offered, string notOffered)
    {
        // The provider both the IDE and the language server complete through.
        const string text = "<p>Hi</p>\n@Mo";
        var provider = new Basalt.Workspace.Web.VbHtmlCompletionProvider();

        var items = await provider.GetCompletionsAsync(
            new Basalt.Extensibility.LanguageDocument(Path.Combine(Path.GetTempPath(), file), text), text.Length);

        Assert.Contains(items, item => item.DisplayText == offered);
        Assert.DoesNotContain(items, item => item.DisplayText == notOffered);
        Assert.Contains(items, item => item.DisplayText == "Functions");
    }

    [Fact]
    public void ViewsAndComponentsAreOfferedTheirOwn()
    {
        var view = VbHtmlDirectives.For("Index.vbhtml").Select(directive => directive.Name).ToList();
        var component = VbHtmlDirectives.For("Counter.vbrazor").Select(directive => directive.Name).ToList();

        Assert.Contains("ModelType", view);
        Assert.DoesNotContain("rendermode", view);
        Assert.Contains("rendermode", component);
        Assert.DoesNotContain("ModelType", component);
        Assert.Contains("Code", view);
        Assert.Contains("Code", component);
    }
}
