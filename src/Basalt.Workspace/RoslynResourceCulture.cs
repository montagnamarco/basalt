using System.Globalization;
using System.Reflection;
using Basalt.Core.Localization;

namespace Basalt.Workspace;

/// <summary>
/// Makes Roslyn's own strings, the titles of its quick actions above all, come in the IDE's language.
/// </summary>
/// <remarks>
/// Roslyn writes a code action's title when the provider registers it, and
/// takes the text from its resource files in the thread's UI culture. On an
/// Italian machine the IDE showed "Genera il metodo…" next to English menus,
/// because the operating system's language, not the interface language,
/// decided it. The providers run on pool threads, and their continuations hop
/// between threads, so setting the thread's culture around them would not
/// hold. Roslyn's resource classes carry a settable Culture instead; this sets
/// it to the interface language. Reflection, because those classes are internal.
/// </remarks>
/// <remarks>
/// This never throws. It is a best effort on top of Roslyn's internals: if a
/// Roslyn assembly is missing, or its types have changed, it does nothing and
/// the quick actions still work, in the operating system's language. The
/// lookup is cached, so a failure there must not be cached as well; every
/// step below catches, and the cached value is always a list, possibly empty.
/// </remarks>
internal static class RoslynResourceCulture
{
    private static readonly string[] Assemblies =
    [
        "Microsoft.CodeAnalysis.Features",
        "Microsoft.CodeAnalysis.VisualBasic.Features",
        "Microsoft.CodeAnalysis.Workspaces",
        "Microsoft.CodeAnalysis.VisualBasic.Workspaces",
    ];

    private static readonly Lazy<IReadOnlyList<PropertyInfo>> Cultures = new(FindCultureProperties);

    private static readonly object Gate = new();

    /// <summary>Points Roslyn's resource strings at the interface language, which may have changed since last time.</summary>
    public static void UseInterfaceLanguage()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Localizer.InterfaceLanguage);

            lock (Gate)
            {
                foreach (var property in Cultures.Value)
                {
                    try
                    {
                        property.SetValue(null, culture);
                    }
                    catch (Exception)
                    {
                        // One resource class that refuses the value must not stop the others.
                    }
                }
            }
        }
        catch (Exception)
        {
            // Roslyn's strings stay in the operating system's language; quick actions still work.
        }
    }

    /// <summary>The static Culture property of every Roslyn resource class in the Features and Workspaces assemblies.</summary>
    /// <remarks>Never throws: the result is cached, so a failure here would be remembered for the process.</remarks>
    private static IReadOnlyList<PropertyInfo> FindCultureProperties()
    {
        var found = new List<PropertyInfo>();

        try
        {
            foreach (var name in Assemblies)
                found.AddRange(CulturePropertiesIn(name));
        }
        catch (Exception)
        {
            // Keep whatever was found; an empty list leaves Roslyn's own culture in charge.
        }

        return found;
    }

    private static IEnumerable<PropertyInfo> CulturePropertiesIn(string assemblyName)
    {
        Type[] types;

        try
        {
            types = Assembly.Load(assemblyName).GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A partly loadable assembly still yields the types that did load.
            types = ex.Types.OfType<Type>().ToArray();
        }
        catch (Exception)
        {
            // A language that is not installed simply has no strings to set.
            return [];
        }

        var properties = new List<PropertyInfo>();

        foreach (var type in types)
        {
            try
            {
                if (!type.IsClass || !type.Name.EndsWith("Resources", StringComparison.Ordinal)) continue;

                var property = type.GetProperty(
                    "Culture", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (property is { CanWrite: true } && property.PropertyType == typeof(CultureInfo))
                    properties.Add(property);
            }
            catch (Exception)
            {
                // A type that cannot be inspected is not a resource class we can set.
            }
        }

        return properties;
    }
}
