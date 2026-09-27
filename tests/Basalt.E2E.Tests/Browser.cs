using System.Runtime.InteropServices;
using Microsoft.Playwright;

namespace Basalt.E2E.Tests;

/// <summary>
/// A headless browser for the tests in a class.
/// </summary>
/// <remarks>
/// The Edge already installed on Windows first, so a developer's machine
/// needs no browser download; then Playwright's own Chromium, which CI
/// installs. Without either the tests are skipped with the reason, as the
/// debugger tests are without netcoredbg, except on CI, where a missing
/// browser is a broken build rather than a test to skip.
/// </remarks>
public sealed class Browser : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser? Instance { get; private set; }

    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        var attempts = new List<BrowserTypeLaunchOptions>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            attempts.Add(new BrowserTypeLaunchOptions { Channel = "msedge" });

        attempts.Add(new BrowserTypeLaunchOptions());

        var reasons = new List<string>();

        foreach (var options in attempts)
        {
            try
            {
                Instance = await _playwright.Chromium.LaunchAsync(options);
                return;
            }
            catch (PlaywrightException ex)
            {
                reasons.Add($"{options.Channel ?? "chromium"}: {ex.Message.Split('\n')[0]}");
            }
        }

        Unavailable = "No browser to drive: " + string.Join("; ", reasons);

        if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 })
            throw new InvalidOperationException(Unavailable);
    }

    /// <summary>A fresh page, or the test skipped when there is no browser.</summary>
    public async Task<IPage> NewPageAsync()
    {
        if (Instance is null) Assert.Skip(Unavailable ?? "No browser.");

        return await Instance.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Instance is not null) await Instance.DisposeAsync();

        _playwright?.Dispose();
    }
}
