using Microsoft.Playwright;

namespace Basalt.E2E.Tests;

/// <summary>
/// Clicker.vbrazor.css styles Clicker's paragraphs and no one else's.
/// </summary>
/// <remarks>
/// Read from the browser's computed style: that the bundle is served, that
/// its selectors carry the component's scope, and that the component's
/// elements carry the same scope, all have to hold at once.
/// </remarks>
public sealed class CssIsolationTests(SampleSite site, Browser browser)
    : IClassFixture<SampleSite>, IClassFixture<Browser>
{
    private const string Green = "rgb(0, 128, 0)";

    // Retried until it holds: the bundle is a stylesheet of its own, and a
    // colour read once, straight after navigating, was sometimes read first.
    private static readonly LocatorAssertionsToHaveCSSOptions Patiently = new() { Timeout = 20_000 };

    [Fact]
    public async Task AComponentsStylesheetAppliesToItAlone()
    {
        var page = await browser.NewPageAsync();

        await page.GotoAsync(site.Address + "/clicker");
        await Assertions.Expect(page.Locator("p").First).ToHaveCSSAsync("color", Green, Patiently);
        await browser.CaptureAsync(page, "css-scoped-component");

        await page.GotoAsync(site.Address + "/signup");
        await Assertions.Expect(page.Locator("#ready")).ToBeAttachedAsync(new() { Timeout = 20_000 });

        await page.Locator("#name").FillAsync("Ada");
        await page.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#thanks")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Assertions.Expect(page.Locator("#thanks")).Not.ToHaveCSSAsync("color", Green, Patiently);
        await browser.CaptureAsync(page, "css-unrelated-component");
    }
}
