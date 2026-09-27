using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basalt.E2E.Tests;

/// <summary>
/// The MVC sample, served by Kestrel on a free local port.
/// </summary>
/// <remarks>
/// Kestrel rather than the in-memory test server: the browser has to reach
/// the site over a real socket, and Blazor's circuit over a real WebSocket.
/// </remarks>
public sealed class SampleSite : WebApplicationFactory<Basalt.Sample.Mvc.Program>, IAsyncLifetime
{
    public string Address { get; private set; } = "";

    public ValueTask InitializeAsync()
    {
        UseKestrel(port: 0);
        StartServer();

        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;

        Address = addresses.Addresses.First().TrimEnd('/');

        return ValueTask.CompletedTask;
    }
}
