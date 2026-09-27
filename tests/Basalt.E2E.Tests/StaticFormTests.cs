using Microsoft.Playwright;

namespace Basalt.E2E.Tests;

/// <summary>
/// A statically rendered form: no circuit, a plain POST, the model filled by
/// [SupplyParameterFromForm] and validated on the server.
/// </summary>
public sealed class StaticFormTests(SampleSite site, Browser browser)
    : IClassFixture<SampleSite>, IClassFixture<Browser>
{
    private static readonly LocatorAssertionsToHaveTextOptions Text = new() { Timeout = 20_000 };

    [Fact]
    public async Task APostedFormIsValidatedAndHandledOnTheServer()
    {
        var page = await browser.NewPageAsync();

        await page.GotoAsync(site.Address + "/subscribe");

        await page.Locator("#email").FillAsync("nope");
        await page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(page.Locator(".validation-message").First).ToHaveTextAsync("That is not an e-mail address.", Text);

        await page.Locator("#email").FillAsync("ada@example.com");
        await page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(page.Locator("#subscribed")).ToHaveTextAsync("Subscribed ada@example.com", Text);
    }
}
