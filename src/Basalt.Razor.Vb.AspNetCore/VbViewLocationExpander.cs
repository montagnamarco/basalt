using Microsoft.AspNetCore.Mvc.Razor;

namespace Basalt.Razor.Vb.AspNetCore;

/// <summary>
/// Teaches the Razor view engine to look for <c>.vbhtml</c> files.
/// </summary>
/// <remarks>
/// Without this, a Visual Basic web application fails at runtime with
/// "Searched locations: /Views/Home/Index.cshtml" while the .vbhtml view sits
/// unread beside it. The engine's location formats are fixed to .cshtml, and
/// this is the supported way to add to them.
/// </remarks>
public sealed class VbViewLocationExpander : IViewLocationExpander
{
    /// <summary>Nothing varies the locations, so nothing is contributed to the cache key.</summary>
    public void PopulateValues(ViewLocationExpanderContext context)
    {
    }

    /// <inheritdoc />
    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        ArgumentNullException.ThrowIfNull(viewLocations);

        // The Visual Basic view comes first so that it wins when a project
        // holds both, which happens while a site is being ported one view at
        // a time. The C# location is kept: a mixed project must keep working.
        foreach (var location in viewLocations)
        {
            if (location.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            {
                yield return string.Concat(
                    location.AsSpan(0, location.Length - ".cshtml".Length), ".vbhtml");
            }

            yield return location;
        }
    }
}
