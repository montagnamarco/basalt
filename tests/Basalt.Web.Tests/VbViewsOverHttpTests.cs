using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Basalt.Web.Tests;

/// <summary>
/// A Visual Basic MVC site answering real requests.
/// </summary>
/// <remarks>
/// The point of the whole exercise, and the only test that can prove it: a
/// view can compile, and its class can exist, while MVC never finds it —
/// which is exactly what happened before, with the engine searching only for
/// .cshtml. Nothing short of a request over HTTP catches that.
/// </remarks>
public sealed class VbViewsOverHttpTests
    : IClassFixture<WebApplicationFactory<Basalt.Sample.Mvc.Program>>
{
    private readonly WebApplicationFactory<Basalt.Sample.Mvc.Program> _factory;

    public VbViewsOverHttpTests(WebApplicationFactory<Basalt.Sample.Mvc.Program> factory) =>
        _factory = factory;

    private async Task<string> GetAsync(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        // The body carries the reason when it is not OK; without it the
        // failure is a bare status code and says nothing about why.
        if (response.StatusCode != HttpStatusCode.OK)
            Assert.Fail(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task AnAreaAndTheRootEachServeTheirOwnViewOfTheSameName()
    {
        // Areas/Office/Views/Home/Index and Views/Home/Index. They generated
        // the same class, and the view was found by the end of its type name,
        // so one of them could never be served.
        var area = await GetAsync("/Office");
        var root = await GetAsync("/");

        Assert.Contains("<h1>Area home</h1>", area);
        Assert.DoesNotContain("Area home", root);
        Assert.Contains("<h1>Hello Ada</h1>", root);
    }

    [Fact]
    public async Task APageAnswersOnTheRouteItsPageDirectiveNames()
    {
        // @Page "{id:int}" was parsed and dropped: the page answered only at
        // /Item, with no id, and /Item/5 was a 404.
        var html = await GetAsync("/Item/5");

        Assert.Contains("<h1>Item 5</h1>", html);

        // And the constraint holds.
        var notANumber = await _factory.CreateClient()
            .GetAsync("/Item/five", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, notANumber.StatusCode);
    }

    /// <summary>
    /// The element a regular expression finds, for tests that check its
    /// attributes without depending on their order.
    /// </summary>
    private static string Element(string html, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, pattern);

        Assert.True(match.Success, $"No match for {pattern} in:\n{html}");

        return match.Value;
    }

    [Fact]
    public async Task AFormGetsItsAntiforgeryTokenFromTheTagHelper()
    {
        // FormTagHelper and RenderAtEndOfFormTagHelper, as in a .cshtml: the
        // hidden token is added without the view asking.
        var html = await GetAsync("/Home/Helpers");

        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("action=\"/Home/Helpers\"", Element(html, "<form[^>]*>"));
    }

    [Fact]
    public async Task ValidationAttributesComeFromTheModelsMetadata()
    {
        // [Required] and [Range] become data-val attributes, which the
        // textual rewriting never produced; the label reads [Display].
        var html = await GetAsync("/Home/Helpers");

        var customer = Element(html, "<input[^>]*name=\"Customer\"[^>]*>");
        var quantity = Element(html, "<input[^>]*name=\"Quantity\"[^>]*>");

        Assert.Contains("data-val-required", customer);
        Assert.Contains("data-val-range", quantity);
        Assert.Contains("type=\"number\"", quantity);
        Assert.Contains(">Customer name</label>", html);
        Assert.Contains("data-valmsg-for=\"Customer\"", html);
    }

    [Fact]
    public async Task ASelectListsItsItemsWithTheValueSelected()
    {
        var html = await GetAsync("/Home/Helpers");

        var select = Element(html, "<select[^>]*>[\\s\\S]*?</select>");

        Assert.Contains("<option value=\"red\">Red</option>", select);
        Assert.Contains("<option selected=\"selected\" value=\"green\">Green</option>", select);
    }

    [Fact]
    public async Task RouteValuesPartialsEnvironmentAndTheSitesOwnTagHelpers()
    {
        var html = await GetAsync("/Home/Helpers");

        // asp-route-id fills RouteValues, one entry per prefixed attribute.
        Assert.Contains("href=\"/Home/Helpers/42\"", html);

        // <partial> renders the partial.
        Assert.Contains("<span class=\"badge\">VB</span>", html);

        // <environment> keeps only what applies (the tests run in Development).
        Assert.Contains("in development", html);
        Assert.DoesNotContain("not development", html);

        // A tag helper written in Visual Basic in the site itself.
        Assert.Contains("<strong>QUIET PLEASE!</strong>", html);

        // And no asp-* attribute reaches the browser.
        Assert.DoesNotContain("asp-", html);
    }

    [Fact]
    public async Task AnAttributeWithOneValueIsConditionalOnATagHelperElementToo()
    {
        // disabled="@False" rendered disabled="False", which disables the
        // field; as in a .cshtml it is left out, and True writes its name.
        var html = await GetAsync("/Home/Helpers");

        Assert.DoesNotContain("disabled", Element(html, "<input[^>]*id=\"off\"[^>]*>"));
        Assert.Contains("readonly=\"readonly\"", Element(html, "<input[^>]*id=\"on\"[^>]*>"));
    }

    [Fact]
    public async Task TagStructureCommentsAndAttributeNamesFollowRazor()
    {
        var html = await GetAsync("/Home/Helpers");

        // <partial name="_Badge"> without a slash: PartialTagHelper declares no
        // end tag, and the element was left unbound waiting for one.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<span class=\"badge\">VB</span>").Count);

        // A tag helper inside an HTML comment does not run.
        Assert.Contains("<!-- <shout text=\"commented\"></shout> -->", html);

        // LevelID is level-id, as Razor names it.
        Assert.Contains("<strong>LEVEL!!!</strong>", html);
    }

    [Fact]
    public async Task ATextAreaShowsItsValueAsContent()
    {
        var html = await GetAsync("/Home/Helpers");

        // ASP.NET Core writes a line break before the value, so a value that
        // itself starts with one survives the browser dropping the first.
        Assert.EndsWith("Ada</textarea>", Element(html, "<textarea[^>]*>[^<]*</textarea>"));
    }

    [Fact]
    public async Task AViewComponentRendersFromAViewAndFromAPage()
    {
        // Its own view is a .vbhtml under Views/Shared/Components, found
        // through the same expander. From a page it needs Component on the
        // page class, which pages did not have.
        var view = await GetAsync("/");
        var page = await GetAsync("/Stock");

        Assert.Contains("<span class=\"stock\">In stock: 3</span>", view);
        Assert.Contains("<span class=\"stock\">In stock: 7</span>", page);
    }

    [Fact]
    public async Task AViewReadsItsOwnResourcesThroughIViewLocalizer()
    {
        // Views.Home.Localized.resx under Resources, found by the view's path
        // as for a .cshtml.
        var html = await GetAsync("/Home/Localized");

        Assert.Contains("<h1>Hello from a resource</h1>", html);
    }

    [Fact]
    public async Task ALayoutDirectiveChoosesTheLayout()
    {
        // @Layout "_AdminLayout" used to be refused with VBH008, and the only
        // way to choose a layout was an assignment in a code block.
        var html = await GetAsync("/Plain");

        Assert.Contains("<header>Admin area</header>", html);
        Assert.Contains("Laid out by its own directive", html);
    }

    [Fact]
    public void TheCSharpViewFactoryIsLeftInPlace()
    {
        // MVC takes the last IRazorPageFactoryProvider registered. AddVbViews
        // used to add one of its own, which answered nothing for .cshtml and
        // so replaced the C# factory: a site ported one view at a time lost
        // every C# view. Visual Basic views now go through MVC's factory.
        var providers = _factory.Services
            .GetServices<Microsoft.AspNetCore.Mvc.Razor.IRazorPageFactoryProvider>()
            .ToList();

        var provider = Assert.Single(providers);

        Assert.Equal("DefaultRazorPageFactoryProvider", provider.GetType().Name);
    }

    [Fact]
    public async Task AVisualBasicViewIsServed()
    {
        var html = await GetAsync("/");

        Assert.Contains("<h1>Hello Ada</h1>", html);
    }

    [Fact]
    public async Task TheModelReachesTheView()
    {
        // Typed, through RazorPage(Of TModel): the number comes from the
        // controller, not from the template.
        var html = await GetAsync("/");

        Assert.Contains("Orders: 3", html);
    }

    [Fact]
    public async Task ControlFlowRuns()
    {
        var html = await GetAsync("/");

        Assert.Contains("VIP customer", html);

        // The keyword that closes the block must not reach the page.
        Assert.DoesNotContain("End If", html);
    }

    [Fact]
    public async Task ViewsAreFoundWhenTheEntryAssemblyIsNotTheSite()
    {
        // What this whole class runs under: WebApplicationFactory makes the
        // test project the entry assembly, so a lookup that trusted it found
        // no views and reported "view not found" — which reads as a path
        // problem and sends the reader to the wrong place entirely.
        Assert.NotEqual(
            typeof(Basalt.Sample.Mvc.Program).Assembly,
            System.Reflection.Assembly.GetEntryAssembly());

        var html = await GetAsync("/");

        Assert.Contains("Hello Ada", html);
    }

    [Fact]
    public async Task TheLayoutWrapsThePage()
    {
        var html = await GetAsync("/");

        Assert.Contains("<title>Basalt VB</title>", html);
        Assert.Contains("<header>Sample site</header>", html);

        // RenderBody put the view inside the layout, not beside it.
        Assert.Contains("Hello Ada", html);
        Assert.DoesNotContain("RenderBody", html);
    }

    [Fact]
    public async Task ViewStartAppliesWithoutTheViewAskingForIt()
    {
        // The view names no layout; _ViewStart does.
        var html = await GetAsync("/");

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
    }

    [Fact]
    public async Task TheLayoutDoesNotWrapItself()
    {
        // _ViewStart applies to the pages inside a layout, never to the
        // layout itself: the engine rejects that as a circular reference and
        // the whole site returns 500 — every page, not just the one.
        var html = await GetAsync("/");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<html>"));
    }

    [Fact]
    public async Task ViewImportsLetsAViewNameItsModelBriefly()
    {
        // The sample's view says "@ModelType Customer", with no namespace:
        // it resolves only because _ViewImports imported it. If the cascade
        // stopped working the site would not compile, so reaching a rendered
        // model proves it end to end.
        var html = await GetAsync("/");

        Assert.Contains("Hello Ada", html);
    }

    [Fact]
    public async Task TheNearestViewStartWins()
    {
        // Views/_ViewStart sets _Layout; Views/Admin/_ViewStart sets
        // _AdminLayout. The nearer one must win, or a controller cannot have
        // a layout of its own — which is most of the point of the cascade.
        var html = await GetAsync("/Admin");

        Assert.Contains("<header>Admin area</header>", html);
        Assert.DoesNotContain("Sample site", html);
    }

    [Fact]
    public async Task TheOuterViewStartStillAppliesElsewhere()
    {
        // The fix must not swap one winner for the other: a folder with no
        // _ViewStart of its own keeps inheriting from above.
        var html = await GetAsync("/");

        Assert.Contains("<header>Sample site</header>", html);
    }

    [Fact]
    public async Task ASectionRendersWhereTheLayoutAsksForIt()
    {
        // The view declares the section at the bottom; the layout renders it
        // inside <footer>. Only the layout knows where it belongs, which is
        // the whole reason sections are deferred.
        var html = await GetAsync("/");

        var footer = html.IndexOf("<footer>", StringComparison.Ordinal);
        var content = html.IndexOf("page footer", StringComparison.Ordinal);

        Assert.True(footer >= 0, "the layout rendered no footer");
        Assert.True(content > footer, "the section did not land inside the footer");
    }

    [Fact]
    public async Task AnAbsentOptionalSectionIsNotAnError()
    {
        // The admin view declares no Footer section and its layout does not
        // ask for one. A required section that is missing throws, which is
        // right — but an optional one must simply render nothing.
        var html = await GetAsync("/Admin");

        Assert.Contains("Dashboard", html);
    }

    [Fact]
    public async Task APartialRendersThroughTheViewEngine()
    {
        // Html.PartialAsync goes through MVC's own resolution, so the partial
        // is found by the same rules as any other view.
        var html = await GetAsync("/");

        Assert.Contains("""<span class="badge">VB</span>""", html);
    }

    [Fact]
    public async Task UrlActionProducesARealRoute()
    {
        // @Url is injected, not carried by the base class. Without it the
        // view does not compile; with it, the link must be the route MVC
        // would generate, not the text of the call.
        var html = await GetAsync("/");

        Assert.Contains("""<a href="/Admin">admin</a>""", html);
    }

    [Fact]
    public async Task AwaitedCallsAreAwaited()
    {
        // "@Await x" used to parse Await as a variable name and leave the
        // call as literal text on the page.
        var html = await GetAsync("/");

        Assert.DoesNotContain("PartialAsync", html);
        Assert.DoesNotContain("System.Threading.Tasks", html);
    }

    [Fact]
    public async Task AFalseConditionalAttributeDisappears()
    {
        // disabled="False" disables the button: for a boolean attribute the
        // browser reads its presence, not its value. This one follows another
        // conditional attribute on the page, which is what used to break it.
        var html = await GetAsync("/");

        Assert.Contains("<button>", html);
        Assert.DoesNotContain("disabled=\"False\"", html);
    }

    [Fact]
    public async Task OrdinaryAttributesAreUntouched()
    {
        var html = await GetAsync("/");

        Assert.Contains("""<input value="Ada" />""", html);
    }

    [Fact]
    public async Task ARazorPageIsServed()
    {
        // Pages/Hello.vbhtml, routed on its own path with no controller.
        var html = await GetAsync("/Hello");

        Assert.Contains("A Razor Page in Visual Basic", html);
    }

    [Fact]
    public async Task PagesAndControllersLiveTogether()
    {
        // Both discovery mechanisms are active at once, and registering one
        // must not displace the other.
        Assert.Contains("Hello Ada", await GetAsync("/"));
        Assert.Contains("Dashboard", await GetAsync("/Admin"));
        Assert.Contains("Razor Page", await GetAsync("/Hello"));
    }

    [Fact]
    public async Task AnInjectedServiceReachesTheView()
    {
        // @Inject used to be refused outright: "Basalt has no dependency
        // injection for views."
        var html = await GetAsync("/");

        Assert.Contains("injected greeting", html);
    }

    [Fact]
    public async Task APageHandlerRuns()
    {
        // OnGet on a hand-written page model, as a Razor Pages user writes it.
        var html = await GetAsync("/Counter");

        Assert.Contains("Total: 42", html);
    }

    [Fact]
    public async Task APostWithoutItsTokenIsRejected()
    {
        // Antiforgery is on by default for pages, exactly as in C#. Worth a
        // test of its own: were it silently off, every generated site would
        // ship without CSRF protection and nothing would look wrong.
        var response = await _factory.CreateClient()
            .PostAsync("/Contact", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["Name"] = "Ada" }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task APostBindsItsFormAndRunsOnPost()
    {
        var client = _factory.CreateClient();

        var page = await client.GetStringAsync("/Contact", TestContext.Current.CancellationToken);
        Assert.Contains("not posted yet", page);

        var token = System.Text.RegularExpressions.Regex.Match(
            page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

        Assert.NotEmpty(token);

        var response = await client.PostAsync("/Contact", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Name"] = "Ada",
                ["__RequestVerificationToken"] = token,
            }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // BindProperty filled Name, and OnPost read it.
        Assert.Contains("posted by Ada", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TagHelpersProduceRealUrls()
    {
        var html = await GetAsync("/");

        Assert.Contains("""<a href="/Admin">helper link</a>""", html);
        Assert.Contains("""<a href="/Hello">helper page</a>""", html);

        // Nothing left for the browser to ignore.
        Assert.DoesNotContain("asp-", html);
    }

    [Fact]
    public async Task ABoundFieldShowsTheModelsValue()
    {
        // A form that does not show what is already there is visibly wrong on
        // the first edit of an existing record.
        var html = await GetAsync("/");

        // Written by ASP.NET Core's own InputTagHelper now, as in a .cshtml:
        // the type is taken from the property and every attribute is there.
        var input = System.Text.RegularExpressions.Regex.Match(html, "<input[^>]*name=\"Name\"[^>]*>").Value;

        Assert.Contains("value=\"Ada\"", input);
        Assert.Contains("id=\"Name\"", input);
        Assert.Contains("type=\"text\"", input);
    }

    [Fact]
    public async Task ALabelPointsAtItsField()
    {
        var html = await GetAsync("/");

        Assert.Contains("""<label for="Name">""", html);
        Assert.DoesNotContain("asp-for", html);
    }

    [Fact]
    public async Task AComponentAskingForInteractiveServerIsRenderedForTheCircuit()
    {
        // @rendermode InteractiveServer in Clicker.vbrazor. The prerendered
        // markup carries the marker blazor.web.js starts a circuit from;
        // without the attribute Blazor reads, the page is static and the
        // button does nothing.
        var html = await GetAsync("/clicker");

        Assert.Contains("Clicks: 0", html);
        Assert.Matches(@"<!--Blazor:\{[^}]*""type"":""server""", html);
        Assert.Contains("_framework/blazor.web.js", html);
    }
}
