using System.Text.Json;
using Basalt.Extensibility;

namespace Basalt.Tests;

/// <summary>
/// Loading extensions written by somebody else.
/// </summary>
/// <remarks>
/// The rule that shapes all of this: nothing a plugin does may stop the IDE
/// from starting. A broken extension should cost the user that extension, not
/// their development environment.
/// </remarks>
public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-plugins", Guid.NewGuid().ToString("N"));

    public PluginLoaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        ScratchFolder.Delete(_root);
    }

    /// <summary>A host that records what a plugin asked for.</summary>
    private sealed class Recorder : IPluginHost
    {
        public List<string> Commands { get; } = [];
        public List<string> ToolboxItems { get; } = [];
        public List<string> Messages { get; } = [];

        public void AddCommand(string id, string title, Action invoke) => Commands.Add(id);

        public void AddToolboxItem(string displayName, string elementName, string category, string xaml) =>
            ToolboxItems.Add(elementName);

        public void Write(string message) => Messages.Add(message);
    }

    /// <summary>A plugin folder, with whatever manifest is given.</summary>
    private string Folder(string name, object? manifest, bool withAssembly = false)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        if (manifest is not null)
            File.WriteAllText(
                Path.Combine(folder, PluginLoader.ManifestName),
                JsonSerializer.Serialize(manifest));

        if (withAssembly)
        {
            // The test assembly itself, which is a real assembly holding no
            // IPlugin: enough to get past the file checks and reach the
            // reflection, which is the part worth testing.
            File.Copy(
                typeof(PluginLoaderTests).Assembly.Location,
                Path.Combine(folder, "Plugin.dll"));
        }

        return folder;
    }

    [Fact]
    public void FindsNothingWhereThereIsNoPluginFolder()
    {
        // A fresh installation has none, and that is not an error.
        var loader = new PluginLoader();

        loader.LoadFrom(Path.Combine(_root, "absent"), new Recorder());

        Assert.Empty(loader.Plugins);
    }

    [Fact]
    public void ReportsAFolderWithNoManifest()
    {
        var loader = new PluginLoader();

        Folder("nameless", manifest: null);
        loader.LoadFrom(_root, new Recorder());

        var plugin = Assert.Single(loader.Plugins);

        Assert.False(plugin.IsLoaded);
        Assert.Contains("plugin.json", plugin.Error);
    }

    [Fact]
    public void ReportsAManifestThatNamesNoAssembly()
    {
        var loader = new PluginLoader();

        Folder("empty", new { Name = "Empty", Version = "1.0.0" });
        loader.LoadFrom(_root, new Recorder());

        Assert.Contains("assembly", Assert.Single(loader.Plugins).Error);
    }

    [Fact]
    public void ReportsAnAssemblyThatIsNotThere()
    {
        var loader = new PluginLoader();

        Folder("missing", new { Name = "Missing", Assembly = "NotHere.dll" });
        loader.LoadFrom(_root, new Recorder());

        Assert.Contains("NotHere.dll", Assert.Single(loader.Plugins).Error);
    }

    [Fact]
    public void ReportsAnAssemblyWithNoPluginInIt()
    {
        var loader = new PluginLoader();

        Folder("hollow", new { Name = "Hollow", Assembly = "Plugin.dll" }, withAssembly: true);
        loader.LoadFrom(_root, new Recorder());

        Assert.Contains("IPlugin", Assert.Single(loader.Plugins).Error);
    }

    [Fact]
    public void ReadsWhatTheManifestSaysEvenWhenLoadingFails()
    {
        // The name and version are how the user is told which extension is
        // broken; without them the message names a folder.
        var loader = new PluginLoader();

        Folder("described", new { Name = "Nice Plugin", Version = "2.1.0", Assembly = "NotHere.dll" });
        loader.LoadFrom(_root, new Recorder());

        var plugin = Assert.Single(loader.Plugins);

        Assert.Equal("Nice Plugin", plugin.Manifest.Name);
        Assert.Equal("2.1.0", plugin.Manifest.Version);
    }

    [Fact]
    public void CarriesOnAfterOneFails()
    {
        // The whole point: one broken extension must not cost the others.
        var loader = new PluginLoader();

        Folder("a-broken", new { Name = "Broken", Assembly = "NotHere.dll" });
        Folder("b-alsoBroken", manifest: null);

        loader.LoadFrom(_root, new Recorder());

        Assert.Equal(2, loader.Plugins.Count);
        Assert.Equal(2, loader.Failed.Count());
    }

    [Fact]
    public void SurvivesAManifestThatIsNotEvenJson()
    {
        // A file edited by hand is the likeliest thing to be malformed, and
        // an unhandled parse error would take the IDE down at startup.
        var loader = new PluginLoader();

        var folder = Folder("mangled", manifest: null);
        File.WriteAllText(Path.Combine(folder, PluginLoader.ManifestName), "{ not json");

        loader.LoadFrom(_root, new Recorder());

        Assert.False(Assert.Single(loader.Plugins).IsLoaded);
    }
}

/// <summary>
/// The sample plugin, loaded the way the IDE loads one.
/// </summary>
/// <remarks>
/// A real assembly built from real source, found on disk and reflected over:
/// a hand-written fixture would prove the test harness works, not that a
/// plugin somebody writes will.
/// </remarks>
public sealed class SamplePluginTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-sample-plugin", Guid.NewGuid().ToString("N"));

    public SamplePluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        ScratchFolder.Delete(_root);
    }

    private sealed class Recorder : Basalt.Extensibility.IPluginHost
    {
        public List<string> Commands { get; } = [];
        public List<string> ToolboxItems { get; } = [];
        public List<string> Messages { get; } = [];

        public void AddCommand(string id, string title, Action invoke)
        {
            Commands.Add(id);
            invoke();
        }

        public void AddToolboxItem(string displayName, string elementName, string category, string xaml) =>
            ToolboxItems.Add(displayName);

        public void Write(string message) => Messages.Add(message);
    }

    /// <summary>The sample plugin's folder, copied where a plugin would live.</summary>
    private string? Install()
    {
        var built = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "Basalt.Sample.Plugin", "bin", "Debug", "net10.0"));

        var assembly = Path.Combine(built, "Basalt.Sample.Plugin.dll");
        var manifest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "Basalt.Sample.Plugin", "plugin.json"));

        if (!File.Exists(assembly) || !File.Exists(manifest)) return null;

        var folder = Path.Combine(_root, "sample");
        Directory.CreateDirectory(folder);

        File.Copy(assembly, Path.Combine(folder, "Basalt.Sample.Plugin.dll"));
        File.Copy(manifest, Path.Combine(folder, "plugin.json"));

        return folder;
    }

    [Fact]
    public void LoadsAndDoesWhatItSaysItDoes()
    {
        Assert.SkipWhen(Install() is null, "the sample plugin has not been built");

        var loader = new Basalt.Extensibility.PluginLoader();
        var host = new Recorder();

        loader.LoadFrom(_root, host);

        var plugin = Assert.Single(loader.Plugins);

        Assert.True(plugin.IsLoaded, plugin.Error);
        Assert.Equal("Sample Plugin", plugin.Manifest.Name);

        Assert.Contains("sample.hello", host.Commands);
        Assert.Contains("Sample Banner", host.ToolboxItems);
        Assert.Contains(host.Messages, m => m.Contains("loaded", StringComparison.OrdinalIgnoreCase));
    }
}
