using Microsoft.Playwright;

namespace Basalt.E2E.Tests;

/// <summary>
/// An EditForm in a .vbrazor component, validated by DataAnnotations, used
/// in a browser.
/// </summary>
/// <remarks>
/// What compiling cannot show: that ValidationMessage gets the expression of
/// the field it reports on, that InputText gets its ValueExpression, and that
/// OnValidSubmit runs only when the model is valid.
/// </remarks>
public sealed class FormTests(SampleSite site, Browser browser)
    : IClassFixture<SampleSite>, IClassFixture<Browser>
{
    private static readonly LocatorAssertionsToBeVisibleOptions Visible = new() { Timeout = 20_000 };

    private static readonly LocatorAssertionsToHaveTextOptions Text = new() { Timeout = 20_000 };

    [Fact]
    public async Task AnInvalidFormSaysWhyAndAValidOneIsSubmitted()
    {
        var page = await browser.NewPageAsync();

        await page.GotoAsync(site.Address + "/signup");
        await Assertions.Expect(page.Locator("#ready")).ToBeAttachedAsync(new() { Timeout = 20_000 });

        await page.Locator("#name").FillAsync("");
        await page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(page.Locator(".validation-message").First).ToHaveTextAsync("Tell us your name.", Text);
        await Assertions.Expect(page.Locator("#thanks")).ToHaveCountAsync(0);

        await page.Locator("#name").FillAsync("Ada");
        await page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(page.Locator("#thanks")).ToHaveTextAsync("Thanks, Ada.", Text);
    }
}
