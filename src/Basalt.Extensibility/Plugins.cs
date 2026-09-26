using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Basalt.Extensibility;

/// <summary>
/// What a plugin says about itself.
/// </summary>
/// <remarks>
/// A file beside the assembly rather than attributes inside it, so the IDE can
/// see what a plugin is without loading it: a plugin that needs a newer host
/// should be reported, not run and left to fail somewhere deeper.
/// </remarks>
public sealed class PluginManifest
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string? Author { get; set; }

    /// <summary>The assembly to load, relative to the plugin's folder.</summary>
    public string Assembly { get; set; } = "";

    /// <summary>The lowest IDE version this plugin works with.</summary>
    public string? RequiresHost { get; set; }
}

/// <summary>
/// What the IDE offers a plugin to work with.
/// </summary>
/// <remarks>
/// An interface rather than the IDE itself: a plugin that could reach into
/// the window could break it, and every method here is something the IDE can
/// keep working across versions.
/// </remarks>
public interface IPluginHost
{
    /// <summary>Adds a command, reachable from the palette and bindable to a key.</summary>
    void AddCommand(string id, string title, Action invoke);

    /// <summary>Adds a control to the toolbox.</summary>
    void AddToolboxItem(string displayName, string elementName, string category, string xaml);

    /// <summary>Writes a line to the output panel.</summary>
    void Write(string message);
}

/// <summary>What a plugin has to implement to be one.</summary>
public interface IPlugin
{
    /// <summary>Called once, when the plugin is loaded.</summary>
    void Initialize(IPluginHost host);
}

/// <summary>A plugin that was found, whether or not it loaded.</summary>
public sealed record LoadedPlugin(
    PluginManifest Manifest,
    string Folder,
    IPlugin? Instance,
    string? Error)
{
    public bool IsLoaded => Instance is not null;
}

/// <summary>
/// Finding and loading the plugins installed beside the IDE.
/// </summary>
/// <remarks>
/// Each plugin gets its own load context, so two of them can depend on
/// different versions of the same library without one breaking the other —
/// which is otherwise the first thing that goes wrong once there are three
/// plugins.
///
/// Nothing a plugin does may stop the IDE from starting. A plugin that throws
/// on load is reported and skipped: a broken extension should cost the user
/// that extension, not their development environment.
/// </remarks>
public sealed class PluginLoader
{
    private readonly List<LoadedPlugin> _plugins = [];

    /// <summary>Everything found, loaded or not.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>The ones that failed, for whoever tells the user.</summary>
    public IEnumerable<LoadedPlugin> Failed => _plugins.Where(p => !p.IsLoaded);

    /// <summary>The file a plugin describes itself in.</summary>
    public const string ManifestName = "plugin.json";

    /// <summary>
    /// Where plugins are installed.
    /// </summary>
    /// <remarks>
    /// Beside the settings, under the user's own folder: a plugin installed
    /// next to the application would need the permissions the application
    /// was installed with, which on macOS means an administrator prompt to
    /// add an extension.
    ///
    /// BASALT_PLUGINS overrides it, so a test never loads whatever the person
    /// running it happens to have installed.
    /// </remarks>
    public static string DefaultFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("BASALT_PLUGINS") is { Length: > 0 } set)
                return set;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(home, ".basalt", "plugins");
        }
    }

    /// <summary>
    /// Loads every plugin in a folder.
    /// </summary>
    /// <remarks>
    /// One subfolder per plugin, each with its manifest: a flat folder of
    /// assemblies cannot say which file belongs to which plugin, or which of
    /// them are merely dependencies.
    /// </remarks>
    public void LoadFrom(string folder, IPluginHost host)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var directory in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.Ordinal))
            _plugins.Add(Load(directory, host));
    }

    private static LoadedPlugin Load(string folder, IPluginHost host)
    {
        var manifestPath = Path.Combine(folder, ManifestName);

        var manifest = new PluginManifest { Name = Path.GetFileName(folder) };

        try
        {
            if (!File.Exists(manifestPath))
                return new LoadedPlugin(manifest, folder, null, $"No {ManifestName} in this folder.");

            manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? manifest;

            if (manifest.Assembly is not { Length: > 0 })
                return new LoadedPlugin(manifest, folder, null, "The manifest names no assembly.");

            var assemblyPath = Path.Combine(folder, manifest.Assembly);

            if (!File.Exists(assemblyPath))
                return new LoadedPlugin(manifest, folder, null, $"{manifest.Assembly} is not in the folder.");

            // Its own context, with the plugin's folder as the place its
            // dependencies are looked for.
            var context = new PluginContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            var type = assembly
                .GetTypes()
                .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

            if (type is null)
            {
                // Nothing of it will ever run, so the context goes: otherwise
                // the file stays open until the IDE quits, and on Windows the
                // user cannot delete or replace the folder they got wrong.
                context.Unload();

                return new LoadedPlugin(manifest, folder, null, "The assembly holds no IPlugin.");
            }

            if (Activator.CreateInstance(type) is not IPlugin plugin)
                return new LoadedPlugin(manifest, folder, null, $"{type.Name} could not be created.");

            plugin.Initialize(host);

            return new LoadedPlugin(manifest, folder, plugin, null);
        }
        catch (Exception ex)
        {
            // Everything, deliberately: a plugin is somebody else's code, and
            // the IDE has to start whatever it does. Reflection alone can
            // throw a dozen different ways.
            return new LoadedPlugin(manifest, folder, null, ex.Message);
        }
    }

    /// <summary>
    /// A load context per plugin.
    /// </summary>
    /// <remarks>
    /// Two plugins depending on different versions of the same library is the
    /// first thing that goes wrong when they share one context. The IDE's own
    /// assemblies are deliberately *not* isolated: a plugin has to see the
    /// same <see cref="IPlugin"/> the host does, or the type it implements is
    /// a different type and nothing matches.
    /// </remarks>
    private sealed class PluginContext(string assemblyPath)
        : AssemblyLoadContext(Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // Shared with the host, so the interfaces are the same types.
            if (name.Name is { } named
                && (named.StartsWith("Basalt.", StringComparison.Ordinal)
                    || named.StartsWith("Avalonia", StringComparison.Ordinal)))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(name);

            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
