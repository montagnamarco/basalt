using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

namespace Basalt.Razor.Vb.AspNetCore;

/// <summary>
/// Finds the class the source generator wrote for a <c>.vbhtml</c> view and
/// hands the view engine a factory for it.
/// </summary>
/// <remarks>
/// ASP.NET Core compiles .cshtml views through its own Razor build task, which
/// only understands C#. Visual Basic views are compiled instead by the Basalt
/// source generator, into ordinary types in the application's own assembly.
/// This bridges the two: it maps a view path back to the generated type using
/// the same <see cref="ViewNaming"/> rules that produced it.
/// </remarks>
public sealed class VbRazorPageFactoryProvider : IRazorPageFactoryProvider
{
    private const string VbExtension = ".vbhtml";

    private readonly IReadOnlyList<Assembly> _assemblies;
    private readonly ConcurrentDictionary<string, Type?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks for views in the entry assembly.</summary>
    public VbRazorPageFactoryProvider()
        : this(Assembly.GetEntryAssembly() is { } entry ? [entry] : [])
    {
    }

    /// <summary>Looks for views in the given assemblies, for tests and for class libraries.</summary>
    public VbRazorPageFactoryProvider(IReadOnlyList<Assembly> assemblies) =>
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));

    /// <inheritdoc />
    public RazorPageFactoryResult CreateFactory(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var descriptor = new CompiledViewDescriptor { RelativePath = relativePath };

        if (!relativePath.EndsWith(VbExtension, StringComparison.OrdinalIgnoreCase))
        {
            // Not ours. A null factory is how "no such page" is reported;
            // the descriptor is still required, and Success reads from the
            // factory rather than from a flag of its own.
            return new RazorPageFactoryResult(descriptor, null);
        }

        var type = _resolved.GetOrAdd(relativePath, FindGeneratedType);
        if (type is null) return new RazorPageFactoryResult(descriptor, null);

        return new RazorPageFactoryResult(descriptor, () =>
        {
            var page = (IRazorPage)Activator.CreateInstance(type)!;

            // The engine reads this back when resolving layouts and partials
            // relative to the current view; without it, rendering fails with
            // an ArgumentNullException on 'path' rather than anything helpful.
            page.Path = relativePath;
            return page;
        });
    }

    private Type? FindGeneratedType(string relativePath)
    {
        var trimmed = relativePath.TrimStart('/', '\\');
        var withoutExtension = trimmed[..^VbExtension.Length];

        var className = ViewNaming.MakeClassName(Path.GetFileName(withoutExtension));
        var folder = ViewNaming.FolderNamespaceFor(trimmed);

        // The generator's namespace is RootNamespace + the folders below the
        // views root; the root namespace is not known here, so the candidate
        // is matched by its ending instead of built up front.
        var suffix = folder.Length == 0 ? className : $"{folder}.{className}";

        foreach (var assembly in _assemblies)
        {
            foreach (var candidate in assembly.GetTypes())
            {
                if (!typeof(IRazorPage).IsAssignableFrom(candidate)) continue;
                if (candidate.IsAbstract) continue;

                var name = candidate.FullName;
                if (name is null) continue;

                if (name.Equals(suffix, StringComparison.Ordinal) ||
                    name.EndsWith("." + suffix, StringComparison.Ordinal))
                    return candidate;
            }
        }

        return null;
    }
}
