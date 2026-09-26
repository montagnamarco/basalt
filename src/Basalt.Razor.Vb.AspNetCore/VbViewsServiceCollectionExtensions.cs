using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basalt.Razor.Vb.AspNetCore;

/// <summary>
/// Registers everything needed to serve <c>.vbhtml</c> views.
/// </summary>
public static class VbViewsServiceCollectionExtensions
{
    /// <summary>
    /// Makes ASP.NET Core find and render Visual Basic views.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="viewAssemblies">
    /// Where the generated view classes live. Defaults to the entry assembly,
    /// which is right for an ordinary application; pass them explicitly when
    /// views come from a class library or from a test host.
    /// </param>
    /// <remarks>
    /// One call is the whole setup, on purpose: writing a Visual Basic site
    /// should not require knowing how the view engine resolves paths.
    /// </remarks>
    public static IServiceCollection AddVbViews(
        this IServiceCollection services, params Assembly[] viewAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assemblies = viewAssemblies is { Length: > 0 }
            ? viewAssemblies
            : DefaultViewAssemblies();

        return Register(services, assemblies);
    }

    /// <summary>
    /// Where to look for views when the caller did not say.
    /// </summary>
    /// <remarks>
    /// The entry assembly alone is not enough: under WebApplicationFactory and
    /// any other test host the entry assembly is the test project, and the
    /// site's views are never found — the failure reads as "view not found",
    /// which sends the reader looking at paths rather than at assemblies.
    /// Loaded assemblies are searched too, skipping the framework's own.
    /// </remarks>
    private static Assembly[] DefaultViewAssemblies()
    {
        var found = new List<Assembly>();

        if (Assembly.GetEntryAssembly() is { } entry) found.Add(entry);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            if (found.Contains(assembly)) continue;

            var name = assembly.GetName().Name;
            if (name is null) continue;

            // Nothing generated lives in these, and walking their types on
            // every startup is work with no possible result.
            if (name.StartsWith("System.", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                name.StartsWith("netstandard", StringComparison.Ordinal) ||
                name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                name.StartsWith("xunit", StringComparison.Ordinal))
                continue;

            found.Add(assembly);
        }

        return [.. found];
    }

    private static IServiceCollection Register(
        IServiceCollection services, IReadOnlyList<Assembly> assemblies)
    {
        services.Configure<RazorViewEngineOptions>(options =>
            options.ViewLocationExpanders.Add(new VbViewLocationExpander()));

        // Razor Pages are discovered from assemblies registered as compiled
        // Razor parts. The application's own assembly is an ordinary
        // AssemblyPart, which that scan skips: the pages compile, carry their
        // attributes, and are never routed to — a 404 with nothing logged.
        services.AddMvcCore().ConfigureApplicationPartManager(manager =>
        {
            foreach (var assembly in assemblies)
            {
                var alreadyThere = manager.ApplicationParts
                    .OfType<CompiledRazorAssemblyPart>()
                    .Any(part => part.Assembly == assembly);

                if (!alreadyThere)
                    manager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            }
        });

        // No factory of our own. Every view carries [RazorCompiledItem], as a
        // C# view does, so MVC's own factory finds it by path through the
        // compiled Razor parts above. A second IRazorPageFactoryProvider used
        // to be registered here, and MVC takes only the last one: it replaced
        // the C# factory, so a site part way through a port stopped serving
        // its .cshtml views.
        return services;
    }
}
