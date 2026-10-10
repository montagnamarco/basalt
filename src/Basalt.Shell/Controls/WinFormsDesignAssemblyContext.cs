using System.Reflection;
using System.Runtime.Loader;
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
/// never loaded twice. Only the project's own assemblies, and any other library in the
/// output folder, are loaded here.
///
/// They are read as bytes rather than loaded from their path, so the next build can
/// overwrite the files while this context is still alive, on Windows too. A context is
/// unloaded when a newer build replaces it; the designer's test proves it is collected.
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
    private Assembly? _projectAssembly;

    /// <summary>
    /// The assembly names the IDE's default context can provide from the trusted platform list. Together
    /// with the assemblies already loaded there (see <see cref="IsHostAssembly"/>), they resolve from the
    /// default context, so there is one copy of each in the process.
    /// </summary>
    private static readonly HashSet<string> HostNames = BuildHostNames();

    public WinFormsDesignAssemblyContext(string projectAssemblyPath)
        : base("Basalt design: " + Path.GetFileName(projectAssemblyPath), isCollectible: true)
    {
        ProjectAssemblyPath = Path.GetFullPath(projectAssemblyPath);
        _folder = Path.GetDirectoryName(ProjectAssemblyPath)!;
        _projectName = Path.GetFileNameWithoutExtension(ProjectAssemblyPath);

        // A collectible assembly keeps its context alive through a handle the runtime roots
        // until no Assembly or Type of the context is reachable. This field is such a
        // reference, so while it holds the project assembly the context can never be
        // collected: measured, the context survived every GC after Unload. Unload drops it.
        Unloading += _ =>
        {
            lock (_projectLock) _projectAssembly = null;
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
    /// Reads the project assembly now, so that a build which does not load is found on the
    /// background thread that asked for the context rather than on the interface thread.
    /// The context is unloaded again when the load fails.
    /// </summary>
    public static WinFormsDesignAssemblyContext Open(string projectAssemblyPath)
    {
        var context = new WinFormsDesignAssemblyContext(projectAssemblyPath);
        try
        {
            _ = context.ProjectAssembly;
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
    /// The type with this full name in the project, or in another library of its output folder
    /// (a class library the project references). Null when none has it.
    /// </summary>
    public Type? FindType(string fullName)
    {
        var project = TryProjectAssembly();
        if (project is not null && TypeIn(project, fullName) is { } projectType) return projectType;

        foreach (var path in Directory.EnumerateFiles(_folder, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (IsHostAssembly(name) || name.Equals(_projectName, StringComparison.OrdinalIgnoreCase)) continue;
            var library = TryLoadLibrary(name);
            if (library is not null && TypeIn(library, fullName) is { } libraryType) return libraryType;
        }
        return null;
    }

    /// <summary>
    /// Whether the default context can provide the assembly name, so that it is taken from there. A name
    /// the default context cannot provide is read from this context's output folder instead: Microsoft.VisualBasic.Forms
    /// is one, because the IDE does not reference Basalt.Forms.VisualBasic, while the project's build output has it.
    /// </summary>
    internal static bool IsHostAssembly(string name) =>
        HostNames.Contains(name)
        || AssemblyLoadContext.Default.Assemblies.Any(assembly => string.Equals(NameOf(assembly), name, StringComparison.OrdinalIgnoreCase));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || IsHostAssembly(name)) return null;

        var path = Path.Combine(_folder, name + ".dll");
        return File.Exists(path) ? LoadFromFile(path) : null;
    }

    private static string? NameOf(Assembly assembly) => assembly.GetName().Name;

    private Assembly LoadFromFile(string path)
    {
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

    /// <summary>
    /// The library with this name, loaded into this context once. Reading it again would give a second copy of
    /// its types, which no longer match the ones the project's own assemblies were compiled against.
    /// </summary>
    private Assembly? TryLoadLibrary(string name)
    {
        try
        {
            return Assemblies.FirstOrDefault(assembly => string.Equals(NameOf(assembly), name, StringComparison.OrdinalIgnoreCase))
                ?? Load(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            // A native library or one that does not load is not a designer control library.
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

    private static HashSet<string> BuildHostNames()
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
        return names;
    }
}
