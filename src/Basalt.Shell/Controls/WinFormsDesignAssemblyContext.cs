using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// The user's own compiled assemblies for one build output, loaded into the IDE for the
/// WinForms designer so that its toolbox and surface show the controls the user wrote.
/// </summary>
/// <remarks>
/// Type identity is the reason this is a context of its own and not a separate process:
/// a user UserControl has to be a <c>System.Windows.Forms.Control</c> of the designer's
/// own Forms, so every assembly the IDE already has (the Forms, Drawing and Visual Basic
/// Forms assemblies, and every framework assembly) resolves to the default context and is
/// never loaded twice. Only the project's own assembly and the libraries the project uses
/// are loaded here: the libraries its <c>deps.json</c> lists, which are the project references
/// and packages of the build, and not every dll that happens to sit in the output folder.
///
/// They are read as bytes rather than loaded from their path, so the next build can
/// overwrite the files while this context is still alive, on Windows too. A context is
/// unloaded when a newer build replaces it; the designer's test proves it is collected.
///
/// Everything that reads a file or describes a type happens in <see cref="Open"/>, on the thread that
/// calls it (the designer opens a context on a background thread). What the toolbox shows is then read
/// from memory, so rebuilding the toolbox on the interface thread never loads an assembly.
///
/// Accepted limit: this runs user code inside the IDE process. A constructor or paint that
/// throws is contained at the designer's boundary (see WinFormsDesignerSession.UserCode), but
/// an infinite loop in user code still hangs the IDE. The owner accepted that trade-off.
/// </remarks>
internal sealed class WinFormsDesignAssemblyContext : AssemblyLoadContext
{
    private readonly string _folder;
    private readonly string _projectName;
    private readonly object _projectLock = new();

    /// <summary>
    /// The assembly names the IDE's default context can provide, taken when this context is created. Together
    /// with the assemblies already loaded there, they resolve from the default context, so there is one copy of
    /// each in the process. Kept per context so the list is not rebuilt for every name asked for.
    /// </summary>
    private readonly HashSet<string> _hostNames;

    /// <summary>
    /// The libraries that could not be read, with the reason as text. A runtime request for one of them throws the
    /// same reason rather than reading the file again. Only text is kept: an exception object would carry its stack
    /// trace through this context's code, and the cache is cleared on unload so nothing outlives the context.
    /// </summary>
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    private Assembly? _projectAssembly;
    private IReadOnlyList<WinFormsToolboxItem> _projectControls = [];
    private IReadOnlyList<WinFormsDesignLibrary> _libraries = [];
    private IReadOnlyList<WinFormsDesignFailure> _failures = [];
    private string? _dependencyProblem;
    private readonly ConcurrentDictionary<int, int> _readsByThread = new();

    public WinFormsDesignAssemblyContext(string projectAssemblyPath)
        : base("Basalt design: " + Path.GetFileName(projectAssemblyPath), isCollectible: true)
    {
        ProjectAssemblyPath = Path.GetFullPath(projectAssemblyPath);
        _folder = Path.GetDirectoryName(ProjectAssemblyPath)!;
        _projectName = Path.GetFileNameWithoutExtension(ProjectAssemblyPath);
        _hostNames = HostNamesOfThisProcess();

        // A collectible assembly keeps its context alive through a handle the runtime roots
        // until no Assembly or Type of the context is reachable. The fields below hold such
        // references, so while they hold them the context can never be collected: measured,
        // the context survived every GC after Unload. Unload drops them.
        Unloading += _ =>
        {
            lock (_projectLock) _projectAssembly = null;
            _projectControls = [];
            _libraries = [];
            _failures = [];
            _unreadable.Clear();
            WinFormsDesignTimeCaches.Release(this);
        };
    }

    /// <summary>The build output assembly of the project that owns the designed form.</summary>
    public string ProjectAssemblyPath { get; }

    /// <summary>The project's own assembly, read from its bytes on first use.</summary>
    public Assembly ProjectAssembly
    {
        get
        {
            lock (_projectLock) return _projectAssembly ??= LoadFromFile(ProjectAssemblyPath);
        }
    }

    /// <summary>
    /// The public, designable controls of the project's own assembly (a form is not one: the designer adds a form as
    /// its own surface). Empty when the project's types could not be described; see <see cref="FailedLibraries"/>.
    /// </summary>
    public IReadOnlyList<WinFormsToolboxItem> ProjectControls => _projectControls;

    /// <summary>
    /// The libraries the project uses other than itself, each loaded into this context: the class libraries it
    /// references and the packages it depends on, as its build's deps.json lists them. Host assemblies are left out.
    /// A library whose controls cannot be described is listed here with none, so the designer still resolves the
    /// types its forms name.
    /// </summary>
    public IReadOnlyList<WinFormsDesignLibrary> ReferencedLibraries() => _libraries;

    /// <summary>
    /// The libraries and the project that could not be read or described, each with the reason. The designer
    /// writes them to Output once for the build; the rest of the toolbox is unaffected.
    /// </summary>
    public IReadOnlyList<WinFormsDesignFailure> FailedLibraries => _failures;

    /// <summary>
    /// The text of why the libraries of the build are not listed, when the dependency list is missing or does not
    /// read: the project's own controls are still listed. Null when the list read.
    /// </summary>
    public string? DependencyListProblem => _dependencyProblem;

    /// <summary>The number of assemblies this context has read from its folder; for the designer's tests.</summary>
    internal int AssemblyReadsForTests => _readsByThread.Values.Sum();

    /// <summary>The number of assemblies this context has read on the thread with this managed id; for the designer's tests.</summary>
    internal int AssemblyReadsOnThreadForTests(int managedThreadId) =>
        _readsByThread.TryGetValue(managedThreadId, out var reads) ? reads : 0;

    /// <summary>
    /// Reads the project assembly, then describes the project and each library it uses, so that a build which does
    /// not load is found on the background thread that asked for the context rather than on the interface thread.
    /// The context is unloaded again when the project assembly does not load; a library that fails is only recorded.
    /// </summary>
    public static WinFormsDesignAssemblyContext Open(string projectAssemblyPath)
    {
        var context = new WinFormsDesignAssemblyContext(projectAssemblyPath);
        try
        {
            _ = context.ProjectAssembly;
            context.DescribeProjectAndLibraries();
            return context;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>Whether the type comes from one of this context's assemblies.</summary>
    public bool Contains(Type type) => ReferenceEquals(GetLoadContext(type.Assembly), this);

    /// <summary>
    /// Makes every later type lookup throw this exception. A test uses it to make a rebuild fail on purpose, as a lookup in
    /// a vanished output folder did before the libraries were read on open. Never set in the product.
    /// </summary>
    internal Exception? ResolutionFaultForTests { get; set; }

    /// <summary>
    /// The type with this full name in the project, or in one of the libraries it uses. Null when none has it.
    /// </summary>
    public Type? FindType(string fullName)
    {
        if (ResolutionFaultForTests is { } fault) throw fault;

        var project = TryProjectAssembly();
        if (project is not null && TypeIn(project, fullName) is { } projectType) return projectType;

        foreach (var library in _libraries)
        {
            if (TypeIn(library.Assembly, fullName) is { } libraryType) return libraryType;
        }
        return null;
    }

    /// <summary>
    /// Describes the project, then each library the build's deps.json lists. Each one is contained on its own: a
    /// library that does not load, or whose types do not describe, is recorded in <see cref="FailedLibraries"/>
    /// and the others are still listed.
    /// </summary>
    private void DescribeProjectAndLibraries()
    {
        var failures = new List<WinFormsDesignFailure>();

        _projectControls = Contained(_projectName, () => ListedControls(ProjectAssembly), failures) ?? [];

        var libraries = new List<WinFormsDesignLibrary>();
        foreach (var name in DependencyLibraryNames())
        {
            Assembly assembly;
            try
            {
                assembly = ReadLibrary(name);
            }
            catch (Exception exception) when (IsContainable(exception))
            {
                failures.Add(new WinFormsDesignFailure(name, exception.GetBaseException().Message));
                continue;
            }

            var controls = Contained(name, () => ListedControls(assembly), failures) ?? [];
            libraries.Add(new WinFormsDesignLibrary(name, assembly, controls));
        }

        _libraries = libraries;
        _failures = failures;
    }

    /// <summary>
    /// The names, without extension, of the libraries the project's build uses, from <c>&lt;Project&gt;.deps.json</c>. A
    /// name is kept only when its dll is in the output folder, and is never the project itself or a host assembly.
    /// </summary>
    /// <remarks>
    /// The SDK writes this file for every build. Its runtime target section lists the project references (the dll copied
    /// beside the project) and the NuGet packages the project depends on. Listing the folder instead would also pick up
    /// any dll that was copied there for another reason.
    /// </remarks>
    private IReadOnlyList<string> DependencyLibraryNames()
    {
        var fileName = _projectName + ".deps.json";
        var depsPath = Path.Combine(_folder, fileName);
        if (!File.Exists(depsPath))
        {
            _dependencyProblem = Localizer.Get(StringKeys.WinFormsDesignerDependencyListMissing, fileName);
            return [];
        }

        // A list that does not read leaves the project without libraries, not the designer without its controls: the
        // reason is kept for Output, and the project's own controls are still listed.
        try
        {
            return ProjectLibraryNames(depsPath);
        }
        catch (Exception exception) when (IsContainable(exception))
        {
            _dependencyProblem = Localizer.Get(StringKeys.WinFormsDesignerDependencyListUnreadable, fileName, exception.GetBaseException().Message);
            return [];
        }
    }

    /// <summary>
    /// The library names the build's runtime target lists. The project's own references (typed "project", or "reference"
    /// when the reference is a file, as the Basalt retargeting writes them) and its NuGet packages count. A runtime pack
    /// ("runtimepack") is the platform, not a library the user referenced, and is skipped. The RID-specific
    /// <c>runtimeTargets</c> assets are not read; their managed dlls are listed in <c>runtime</c> of the same target.
    /// </summary>
    /// <remarks>Throws when the file does not parse as a dependency list for its runtime target.</remarks>
    private IReadOnlyList<string> ProjectLibraryNames(string depsPath)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(File.ReadAllBytes(depsPath));
        var root = document.RootElement;

        // The target the build was made for: the one whose assets the output holds. The others describe other platforms.
        var runtimeTarget = root.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? string.Empty;
        var libraries = root.GetProperty("libraries");
        var target = root.GetProperty("targets").GetProperty(runtimeTarget);

        foreach (var library in target.EnumerateObject())
        {
            if (!libraries.TryGetProperty(library.Name, out var info) || !info.TryGetProperty("type", out var kind) ||
                kind.ValueKind != JsonValueKind.String || kind.GetString() is not ("project" or "package" or "reference"))
                continue;
            if (library.Value.ValueKind != JsonValueKind.Object ||
                !library.Value.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var asset in runtime.EnumerateObject())
            {
                var file = Path.GetFileName(asset.Name);
                if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Equals(_projectName, StringComparison.OrdinalIgnoreCase) || IsHostAssembly(name)) continue;
                if (File.Exists(Path.Combine(_folder, file))) names.Add(name);
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// The library with this name, read into this context once. An assembly the runtime already loaded here, as the
    /// dependency of another, is used as it is: reading it again would give a second copy of its types.
    /// </summary>
    private Assembly ReadLibrary(string name) =>
        LoadedByName(name)
        ?? Load(new AssemblyName(name))
        ?? throw new FileNotFoundException("The library is not in the build output.", Path.Combine(_folder, name + ".dll"));

    /// <summary>The controls of an assembly that the toolbox lists: public, designable, and not forms.</summary>
    /// <remarks>
    /// Runs on the background thread that opens the context, under <see cref="WinFormsDesignTimeCaches.TypeDescriptorGate"/>:
    /// Describe asks TypeDescriptor for attributes, and TypeDescriptor's tables are also cleared by a context's release
    /// on the interface thread. The two must not overlap, and the lock is the only thing that orders them.
    /// </remarks>
    private static IReadOnlyList<WinFormsToolboxItem> ListedControls(Assembly assembly)
    {
        lock (WinFormsDesignTimeCaches.TypeDescriptorGate)
        {
            return WinFormsToolboxCatalog.Describe(assembly, typeof(Forms.Control))
                .Where(item => !typeof(Forms.Form).IsAssignableFrom(item.ControlType))
                .ToArray();
        }
    }

    /// <summary>
    /// Runs one description and returns its result, or records the failure under the name and returns null. Only
    /// the failures that are not fatal to the process are contained (see <see cref="IsContainable"/>).
    /// </summary>
    private static T? Contained<T>(string name, Func<T> describe, List<WinFormsDesignFailure> failures) where T : class
    {
        try
        {
            return describe();
        }
        catch (Exception exception) when (IsContainable(exception))
        {
            failures.Add(new WinFormsDesignFailure(name, exception.GetBaseException().Message));
            return null;
        }
    }

    /// <summary>
    /// Whether a failure may be recorded and the designer continue. Anything the process cannot recover from is not
    /// contained. Types and assemblies that do not load, a constructor that throws in a static initializer, and a
    /// missing dependency are all ordinary failures of a user's library and are contained.
    /// </summary>
    private static bool IsContainable(Exception exception) => exception is not OutOfMemoryException;

    /// <summary>
    /// Whether the default context can provide the assembly name, so that it is taken from there. A name
    /// the default context cannot provide is read from this context's output folder instead: Microsoft.VisualBasic.Forms
    /// is one, because the IDE does not reference Basalt.Forms.VisualBasic, while the project's build output has it.
    /// </summary>
    private bool IsHostAssembly(string name) => _hostNames.Contains(name);

    private Assembly? LoadedByName(string name) =>
        Assemblies.FirstOrDefault(assembly => string.Equals(NameOf(assembly), name, StringComparison.OrdinalIgnoreCase));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || IsHostAssembly(name)) return null;

        // A library that failed once is not read again: the runtime asks for a dependency each time it needs it.
        if (_unreadable.TryGetValue(name, out var reason)) throw new FileLoadException(reason);

        var path = Path.Combine(_folder, name + ".dll");
        if (!File.Exists(path)) return null;

        try
        {
            return LoadFromFile(path);
        }
        catch (Exception exception) when (IsContainable(exception))
        {
            _unreadable[name] = exception.GetBaseException().Message;
            throw;
        }
    }

    private static string? NameOf(Assembly assembly) => assembly.GetName().Name;

    private Assembly LoadFromFile(string path)
    {
        _readsByThread.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, reads) => reads + 1);
        var assemblyBytes = File.ReadAllBytes(path);
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        return File.Exists(symbolsPath)
            ? LoadFromStream(new MemoryStream(assemblyBytes), new MemoryStream(File.ReadAllBytes(symbolsPath)))
            : LoadFromStream(new MemoryStream(assemblyBytes));
    }

    private Assembly? TryProjectAssembly()
    {
        try { return ProjectAssembly; }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Type? TypeIn(Assembly assembly, string fullName)
    {
        try { return assembly.GetType(fullName, throwOnError: false, ignoreCase: true); }
        catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or FileLoadException or ArgumentException)
        {
            // A type whose base type or attributes do not load (a dependency missing, or one that cannot be loaded
            // into this context) is not the one the form names.
            return null;
        }
    }

    /// <summary>
    /// The names the default context provides: the trusted platform list of this process, the Forms and Drawing
    /// assemblies by the names Microsoft gives them, and whatever the default context has loaded so far.
    /// </summary>
    private static HashSet<string> HostNamesOfThisProcess()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every framework and IDE assembly the process loads from its trusted list.
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            names.Add(Path.GetFileNameWithoutExtension(path));

        // The Forms and Drawing assemblies are named as Microsoft names them, which is also how the
        // user's project refers to them. Name them explicitly in case the trusted list omits one.
        // Microsoft.VisualBasic.Forms is deliberately absent: the IDE does not ship it, so the default
        // context cannot provide it (see IsHostAssembly) and the project's copy is loaded here.
        names.Add(typeof(Forms.Control).Assembly.GetName().Name!);
        names.Add(typeof(System.Drawing.Color).Assembly.GetName().Name!);
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (NameOf(assembly) is { } name) names.Add(name);
        }
        return names;
    }
}

/// <summary>
/// A library of the build output that the project uses: its file name, its assembly in the design context, and the
/// controls the toolbox lists from it (empty when they could not be described).
/// </summary>
internal sealed record WinFormsDesignLibrary(string Name, Assembly Assembly, IReadOnlyList<WinFormsToolboxItem> Controls);

/// <summary>A library, or the project itself, that could not be read or described: its name and the reason, for Output.</summary>
internal sealed record WinFormsDesignFailure(string Name, string Message);
