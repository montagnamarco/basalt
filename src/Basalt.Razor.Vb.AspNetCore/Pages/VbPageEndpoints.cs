using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Basalt.Web;

/// <summary>
/// Routes <c>.vbp</c> pages by their path, the way a web server serves files.
/// </summary>
public static class VbPageEndpoints
{
    /// <summary>
    /// Maps every page in the given assemblies to its own URL.
    /// </summary>
    /// <remarks>
    /// One call, and a page added later needs no second one: the file's own
    /// path is its route, which is the whole point of working this way. A
    /// page at Pages/Shop/Cart.vbp answers at /shop/cart.
    /// </remarks>
    public static IEndpointRouteBuilder MapVbPages(
        this IEndpointRouteBuilder endpoints, params Assembly[] pageAssemblies)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var assemblies = pageAssemblies is { Length: > 0 }
            ? pageAssemblies
            : Assembly.GetEntryAssembly() is { } entry ? [entry] : Array.Empty<Assembly>();

        foreach (var (route, type) in Discover(assemblies))
        {
            var pageType = type;

            // GET and POST both: a page with a form posts back to itself,
            // which is how a page in this style handles input at all.
            endpoints.MapGet(route, context => RunAsync(pageType, context));
            endpoints.MapPost(route, context => RunAsync(pageType, context));
        }

        return endpoints;
    }

    /// <summary>
    /// Runs one page for one request.
    /// </summary>
    /// <remarks>
    /// Built in memory and written once: ASP.NET Core forbids synchronous
    /// writes to the response body, and a page is written synchronously by
    /// design — that is what makes the generated code read like the file the
    /// author wrote. Buffering keeps both, and lets a page change the status
    /// or redirect after it has already written something.
    /// </remarks>
    private static async Task RunAsync(Type pageType, HttpContext context)
    {
        var page = (VbPage)Activator.CreateInstance(pageType)!;

        var buffer = new StringWriter();

        await page.ExecuteAsync(context, buffer).ConfigureAwait(false);

        // A page that redirected has nothing to say in its body.
        if (context.Response.HasStarted) return;

        var html = buffer.ToString();

        if (context.Response.StatusCode is >= 300 and < 400) return;

        context.Response.ContentType = "text/html; charset=utf-8";

        await context.Response.WriteAsync(html).ConfigureAwait(false);
    }

    /// <summary>
    /// The pages in an assembly, and the route each answers on.
    /// </summary>
    /// <remarks>
    /// Read from the attribute the generator writes rather than from the type
    /// name: a class called Shop_Cart says nothing about whether the folder
    /// was Shop or the file was named with an underscore.
    /// </remarks>
    private static IEnumerable<(string Route, Type Type)> Discover(
        IReadOnlyList<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in Types(assembly))
            {
                if (type.IsAbstract || !typeof(VbPage).IsAssignableFrom(type)) continue;

                var attribute = type.GetCustomAttribute<VbPageRouteAttribute>();

                if (attribute is null) continue;

                yield return (attribute.Route, type);
            }
        }
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // An assembly with a missing dependency still exposes the types
            // that did load, and the pages are usually among them.
            return ex.Types.OfType<Type>();
        }
    }
}

/// <summary>The URL a generated page answers on.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class VbPageRouteAttribute : Attribute
{
    public VbPageRouteAttribute(string route) => Route = route;

    /// <summary>The route, as "/shop/cart".</summary>
    public string Route { get; }
}
