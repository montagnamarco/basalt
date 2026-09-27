using Microsoft.Playwright;

namespace Basalt.E2E.Tests;

/// <summary>
/// A .vbrazor component with @rendermode InteractiveServer, used in a browser.
/// </summary>
/// <remarks>
/// What the HTTP tests cannot see: the page prerenders the same whether the
/// button works or not. Here the click goes over Blazor's circuit and the
/// component has to answer it.
/// </remarks>
public sealed class InteractiveComponentTests(SampleSite site, Browser browser)
    : IClassFixture<SampleSite>, IClassFixture<Browser>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Opens /clicker and waits until the circuit answers: clicks sent before
    /// it is connected are lost, so the first click is repeated until one
    /// counts.
    /// </summary>
    private async Task<IPage> OpenConnectedAsync()
    {
        var page = await browser.NewPageAsync();

        await page.GotoAsync(site.Address + "/clicker");

        var clicks = page.Locator("p").First;
        var deadline = DateTime.UtcNow + Patience;

        while (await clicks.TextContentAsync() == "Clicks: 0")
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The circuit never answered a click.");

            await page.Locator("button").First.ClickAsync();
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return page;
    }

    [Fact]
    public async Task AClickOnALambdaHandlerIsCounted()
    {
        // @onclick="Sub() count += 1"
        var page = await OpenConnectedAsync();
        var clicks = page.Locator("p").First;

        var before = int.Parse((await clicks.TextContentAsync())!.Replace("Clicks: ", ""));

        await page.Locator("button").First.ClickAsync();

        await Assertions.Expect(clicks).ToHaveTextAsync($"Clicks: {before + 1}",
            new() { Timeout = (float)Patience.TotalMilliseconds });
        await browser.CaptureAsync(page, "clicker-count");
    }

    [Fact]
    public async Task TypingUpdatesABindingOnInput()
    {
        // @bind="name" @bind:event="oninput": each keystroke, not on leaving.
        var page = await OpenConnectedAsync();

        await page.Locator("input").PressSequentiallyAsync("Ada");

        await Assertions.Expect(page.Locator("#greeting")).ToHaveTextAsync("Hello Ada",
            new() { Timeout = (float)Patience.TotalMilliseconds });
        await browser.CaptureAsync(page, "clicker-input-binding");
    }

    [Fact]
    public async Task ACollocatedModuleIsImportedAndCalled()
    {
        // Clicker.vbrazor.js, imported with IJSRuntime from
        // ./Components/Clicker.vbrazor.js: the package serves it as the SDK
        // serves a .razor.js, and the component calls into it.
        var page = await OpenConnectedAsync();

        await page.Locator("input").PressSequentiallyAsync("ada");
        await page.Locator("#shout").ClickAsync();

        await Assertions.Expect(page.Locator("#shouted")).ToHaveTextAsync("ADA!",
            new() { Timeout = (float)Patience.TotalMilliseconds });
        await browser.CaptureAsync(page, "clicker-javascript-module");
    }
}
