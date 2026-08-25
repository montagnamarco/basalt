using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The tag helper attributes that carry weight in an ordinary site.
/// </summary>
/// <remarks>
/// Not the framework's mechanism, which parses markup into a tree and runs a
/// class over each element: Basalt keeps markup as text. These are rewritten
/// in place instead. Leaving them alone would be worse than not supporting
/// them — asp-action reaches the browser as an unknown attribute, the anchor
/// has no href, and the link silently goes nowhere.
/// </remarks>
public sealed class TagHelperTests
{
    private static string Generate(string template)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return VbHtmlCodeWriter.Write(document, "View", "Generated", ViewHost.AspNetCore);
    }

    [Fact]
    public void AnchorRoutingBecomesUrlAction()
    {
        var code = Generate("""<a asp-controller="Home" asp-action="Privacy">x</a>""");

        Assert.Contains("""Write(Url.Action("Privacy", "Home"))""", code);
        Assert.DoesNotContain("asp-", code);
    }

    [Fact]
    public void AnchorPageBecomesUrlPage()
    {
        var code = Generate("""<a asp-page="/Hello">x</a>""");

        Assert.Contains("""Write(Url.Page("/Hello"))""", code);
    }

    [Fact]
    public void AnOmittedControllerMeansTheCurrentOne()
    {
        // Url.Action takes Nothing for "the controller we are already in",
        // which is what Razor does with a missing asp-controller.
        var code = Generate("""<a asp-action="Privacy">x</a>""");

        Assert.Contains("""Url.Action("Privacy", Nothing)""", code);
    }

    [Fact]
    public void FormRoutingBecomesAnAction()
    {
        var code = Generate("""<form asp-controller="Home" asp-action="Save"></form>""");

        Assert.Contains("action=", code);
        Assert.Contains("""Url.Action("Save", "Home")""", code);
    }

    [Fact]
    public void OtherAttributesSurvive()
    {
        var code = Generate("""<a class="nav" asp-action="Privacy" id="p">x</a>""");

        Assert.Contains("""class=""nav""", code);
        Assert.Contains("""id=""p""", code);
    }

    [Fact]
    public void AnInputBoundToAPropertyShowsItsValue()
    {
        // A form that does not show what is already there is visibly wrong on
        // the first edit of an existing record.
        var code = Generate("""<input asp-for="Name" />""");

        // Written as an expression rather than through WriteAttribute: the
        // rewriting produces three pieces — the tag up to value=", the
        // expression, and the rest — which is how every rewritten helper
        // reaches the page.
        Assert.Contains("Write(Model.Name)", code);
        Assert.Contains("""value=""", code);
        Assert.DoesNotContain("asp-for", code);
    }

    [Fact]
    public void ABoundInputPostsBackUnderItsOwnName()
    {
        // Without a name the model binder never sees the field, and the form
        // silently discards whatever was typed.
        var code = Generate("""<input asp-for="Name" />""");

        Assert.Contains("""name=""Name""", code);
        Assert.Contains("""id=""Name""", code);
    }

    [Fact]
    public void AHandWrittenNameIsNotOverwritten()
    {
        var code = Generate("""<input asp-for="Name" name="custom" />""");

        Assert.Contains("""name=""custom""", code);
    }

    [Fact]
    public void ALabelPointsAtItsField()
    {
        var code = Generate("""<label asp-for="Name">Name</label>""");

        Assert.Contains("for=", code);
        Assert.DoesNotContain("asp-for", code);
    }

    [Fact]
    public void AHelperWeDoNotImplementLeavesNoAspAttribute()
    {
        // Dropping it is not support, but an unknown attribute in the page is
        // worse: it looks like it did something.
        var code = Generate("""<span asp-validation-for="Name"></span>""");

        Assert.DoesNotContain("asp-validation-for", code);
    }

    [Fact]
    public void MarkupWithoutHelpersIsUntouched()
    {
        var code = Generate("""<a href="/x" class="y">plain</a>""");

        Assert.Contains("""<a href=""/x"" class=""y"">plain</a>""", code);
    }

    [Fact]
    public void AHelperBesideAConditionalAttributeIsStillRewritten()
    {
        // The literal in front of a conditional attribute used to be written
        // by a second code path that skipped the rewriting entirely.
        var code = Generate(
            """<button disabled="@locked">go</button><a asp-action="Privacy">x</a>""");

        Assert.Contains("""WriteAttribute("disabled", locked)""", code);
        Assert.Contains("""Url.Action("Privacy", Nothing)""", code);
        Assert.DoesNotContain("asp-action", code);
    }

    [Fact]
    public void TheStandaloneRuntimeLeavesThemAlone()
    {
        // Outside a web application there is no Url helper to call.
        var document = VbHtmlParser.Parse("""<a asp-action="Privacy">x</a>""");
        var code = VbHtmlCodeWriter.Write(document, "View", "Generated");

        Assert.Contains("asp-action", code);
    }
}
