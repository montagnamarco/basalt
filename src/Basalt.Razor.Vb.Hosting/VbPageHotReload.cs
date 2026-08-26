using Basalt.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Basalt.Razor.Vb.Hosting;

/// <summary>
/// Serves <c>.vbpage</c> pages straight from disk, recompiling on every change.
/// </summary>
public static class VbPageHotReload
{
    /// <summary>
    /// Maps the pages folder so a saved file is a reloaded page.
    /// </summary>
    /// <remarks>
    /// For development. In production the build-time generator is the right
    /// thing: the errors arrive before deployment, and no compiler ships with
    /// the site. Guard the call with the environment rather than shipping
    /// both — <c>if app.Environment.IsDevelopment()</c>.
    /// </remarks>
    public static IEndpointRouteBuilder MapVbPagesFromDisk(
        this IEndpointRouteBuilder endpoints, string pagesFolder)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var compiler = new VbPageCompiler(pagesFolder);

        // A catch-all, because which pages exist is a question about the
        // folder as it is now: a route per file would miss every page added
        // after startup, which is exactly what this is for.
        endpoints.MapFallback(async context =>
        {
            var route = context.Request.Path.Value ?? "/";

            var result = FindPage(compiler, route);

            if (result is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!result.Succeeded)
            {
                await ShowProblem(context, route, result.Error).ConfigureAwait(false);
                return;
            }

            var page = (VbPage)Activator.CreateInstance(result.Type!)!;

            var buffer = new StringWriter();

            await page.ExecuteAsync(context, buffer).ConfigureAwait(false);

            if (context.Response.HasStarted) return;
            if (context.Response.StatusCode is >= 300 and < 400) return;

            context.Response.ContentType = "text/html; charset=utf-8";

            await context.Response.WriteAsync(buffer.ToString()).ConfigureAwait(false);
        });

        return endpoints;
    }

    /// <summary>
    /// The page a URL asks for.
    /// </summary>
    /// <remarks>
    /// "/shop/cart" is Shop/Cart.vbpage, and "/shop" is Shop/Index.vbpage — the
    /// convention every web server has had since the first one.
    /// </remarks>
    private static VbPageCompiler.Result? FindPage(VbPageCompiler compiler, string route)
    {
        var trimmed = route.Trim('/');

        foreach (var candidate in Candidates(trimmed))
        {
            var result = compiler.Load(candidate);

            // A missing file gives no type and no error; a broken one gives
            // an error worth showing rather than moving past.
            if (result.Succeeded || result.Error is not null) return result;
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string route)
    {
        if (route.Length == 0)
        {
            yield return "Index.vbpage";
            yield break;
        }

        yield return route + ".vbpage";
        yield return Path.Combine(route, "Index.vbpage");
    }

    /// <summary>
    /// Shows why a page did not compile, in the browser.
    /// </summary>
    /// <remarks>
    /// On the page itself, because that is where the person editing it is
    /// looking. A 500 with the reason in a console they are not watching is
    /// the thing that makes this way of working tiring.
    /// </remarks>
    private static async Task ShowProblem(HttpContext context, string route, string? error)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";

        var escaped = System.Net.WebUtility.HtmlEncode(error ?? "Unknown problem.");
        var where = System.Net.WebUtility.HtmlEncode(route);

        await context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{where}</title></head>
            <body style="font:14px ui-monospace,monospace;padding:2rem">
              <h1 style="font-size:1.1rem">This page did not compile</h1>
              <p style="opacity:.7">{where}</p>
              <pre style="background:#f6f6f6;padding:1rem;overflow:auto">{escaped}</pre>
            </body></html>
            """).ConfigureAwait(false);
    }
}
