using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Classic;
using Basalt.Web;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Razor.Vb.Hosting;

/// <summary>
/// Compiles a <c>.vbpage</c> page while the site is running, and again when the
/// file changes.
/// </summary>
/// <remarks>
/// What ASP and PHP had and ASP.NET Core does not: save the file, reload the
/// browser. The build-time generator is still the right thing for production —
/// errors arrive before deployment and no compiler ships with the site — but
/// in development the wait between a change and seeing it is the whole cost of
/// working this way.
/// </remarks>
public sealed class VbPageCompiler : IDisposable
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, Compiled> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher? _watcher;

    /// <summary>A page as it was last compiled, and the context holding its assembly.</summary>
    private sealed record Compiled(
        Type? Type, string? Error, DateTime WrittenAt, AssemblyLoadContext? Context = null);

    /// <summary>
    /// Watches a folder of pages.
    /// </summary>
    /// <param name="root">The folder holding the .vbpage files.</param>
    /// <param name="watch">
    /// Whether to notice changes on disk. A watcher costs a handle and a
    /// thread, which a site that never edits its pages has no use for.
    /// </param>
    public VbPageCompiler(string root, bool watch = true)
    {
        _root = Path.GetFullPath(root);

        if (!watch || !Directory.Exists(_root)) return;

        _watcher = new FileSystemWatcher(_root, "*.vbpage")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        // Dropped rather than recompiled here: an editor writes a file in
        // several steps, and compiling on the first of them reports errors
        // about a half-written page. The next request compiles it once, when
        // the writing has finished.
        _watcher.Changed += (_, e) => Forget(e.FullPath);
        _watcher.Created += (_, e) => Forget(e.FullPath);
        _watcher.Deleted += (_, e) => Forget(e.FullPath);
        _watcher.Renamed += (_, e) => { Forget(e.OldFullPath); Forget(e.FullPath); };
    }

    /// <summary>References every compiled page is given.</summary>
    public IReadOnlyList<MetadataReference> References { get; init; } = DefaultReferences();

    /// <summary>Namespaces every page imports without asking.</summary>
    /// <remarks>
    /// Microsoft.VisualBasic among them: the command-line compiler adds it
    /// without being asked, and a page generated with vbCrLf in it failed to
    /// compile here for want of a name every other Visual Basic file has.
    /// </remarks>
    public IReadOnlyList<string> Imports { get; init; } =
    [
        "System", "System.Collections.Generic", "System.Linq",
        "System.Threading.Tasks", "Microsoft.VisualBasic", "Basalt.Web",
    ];

    /// <summary>What a page compiled to, or why it did not.</summary>
    public sealed record Result(Type? Type, string? Error)
    {
        public bool Succeeded => Type is not null;
    }

    /// <summary>
    /// The class for a page, compiling it if this is the first time or the
    /// file has changed since.
    /// </summary>
    public Result Load(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));

        // Inside the root, whatever the caller passed: a path with .. in it
        // would otherwise compile and serve any file on the machine.
        if (!path.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return new Result(null, "That page is outside the pages folder.");

        if (!File.Exists(path)) return new Result(null, null);

        var writtenAt = File.GetLastWriteTimeUtc(path);

        if (_pages.TryGetValue(path, out var cached) && cached.WrittenAt == writtenAt)
            return new Result(cached.Type, cached.Error);

        var compiled = Compile(path, writtenAt);

        if (_pages.TryGetValue(path, out var replaced)) Release(replaced);

        _pages[path] = compiled;

        return new Result(compiled.Type, compiled.Error);
    }

    /// <summary>Forgets a page, so the next request compiles it again.</summary>
    public void Forget(string path)
    {
        if (_pages.TryRemove(Path.GetFullPath(path), out var forgotten)) Release(forgotten);
    }

    /// <summary>Forgets every page.</summary>
    public void ForgetAll()
    {
        foreach (var path in _pages.Keys.ToList()) Forget(path);
    }

    /// <summary>
    /// Lets a compiled page's assembly go.
    /// </summary>
    /// <remarks>
    /// Every edit used to load one more assembly that could never leave: a
    /// morning of editing a page was a few hundred of them held for the life
    /// of the process. Unloading is cooperative — a request still rendering
    /// the old page keeps it alive until it finishes — so this is safe to
    /// call while the site is serving.
    /// </remarks>
    private static void Release(Compiled compiled) => compiled.Context?.Unload();

    private Compiled Compile(string path, DateTime writtenAt)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            // Being written as we read it. Not cached, so the next request
            // tries again rather than serving an error for good.
            return new Compiled(null, "The page could not be read; try again.", DateTime.MinValue);
        }

        var page = VbPageParser.Parse(text);

        if (page.Diagnostics.Count > 0)
        {
            var problems = string.Join("\n", page.Diagnostics.Select(d => $"line {d.Line}: {d.Message}"));

            return new Compiled(null, problems, writtenAt);
        }

        var className = ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(path));

        // A namespace of its own per compilation, so a page recompiled after
        // an edit does not collide with the class already loaded: an assembly
        // cannot be unloaded, and two types of the same full name in one
        // process is a conflict nobody can resolve.
        var generation = Interlocked.Increment(ref _generation);
        var namespaceName = $"Basalt.Pages.Live{generation}";

        var code = VbPageWriter.Write(page, className, namespaceName, path);

        var compilation = VisualBasicCompilation.Create(
            $"BasaltPage{generation}",
            [VisualBasicSyntaxTree.ParseText(SourceText.From(code, System.Text.Encoding.UTF8), path: path)],
            References,
            new VisualBasicCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                globalImports: GlobalImport.Parse([.. Imports]),
                optionStrict: OptionStrict.On,
                optionExplicit: true,
                optionInfer: true));

        using var stream = new MemoryStream();
        using var symbols = new MemoryStream();

        // With a PDB, so an exception thrown by a page names the .vbpage and
        // its line — the pragmas the writer emits map it there — and a
        // debugger attached to the site can stop inside the page.
        var emitted = compilation.Emit(
            stream,
            symbols,
            options: new Microsoft.CodeAnalysis.Emit.EmitOptions(
                debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb));

        if (!emitted.Success)
        {
            var errors = emitted.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(Describe);

            return new Compiled(null, string.Join("\n", errors), writtenAt);
        }

        stream.Position = 0;
        symbols.Position = 0;

        // Its own collectible context, released when the page is compiled
        // again. Types it cannot resolve itself come from the site's context,
        // so a page sees the same Basalt.Web and models the site does.
        var context = new AssemblyLoadContext($"BasaltPage{generation}", isCollectible: true);
        var assembly = context.LoadFromStream(stream, symbols);

        var type = assembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract && typeof(VbPage).IsAssignableFrom(t));

        if (type is null)
        {
            context.Unload();

            return new Compiled(null, "The page compiled but produced no page class.", writtenAt);
        }

        return new Compiled(type, null, writtenAt, context);
    }

    private int _generation;

    /// <summary>
    /// A compiler error, said against the page rather than the generated code.
    /// </summary>
    private static string Describe(Diagnostic diagnostic)
    {
        var line = diagnostic.Location.GetMappedLineSpan();

        return line.IsValid
            ? $"line {line.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}"
            : diagnostic.GetMessage();
    }

    /// <summary>
    /// Everything already loaded into the process.
    /// </summary>
    /// <remarks>
    /// A page can use whatever the site can: its models, its services, the
    /// framework. Listing them by hand would mean a page failing to see a
    /// type the rest of the application uses freely.
    /// </remarks>
    private static IReadOnlyList<MetadataReference> DefaultReferences()
    {
        // Loaded on demand, because an assembly nothing has touched yet is
        // not in the list and a page cannot be compiled against what is not
        // there.
        //
        // Microsoft.VisualBasic: every generated page uses vbCrLf, and an
        // ASP.NET Core site loads it for no other reason. VbPage itself: the
        // base class of every page, which a host that has served none has
        // never had cause to load.
        foreach (var name in new[] { "Microsoft.VisualBasic", "Microsoft.VisualBasic.Core" })
        {
            try
            {
                Assembly.Load(name);
            }
            catch (Exception)
            {
                // Not on this runtime; a page needing it will say so.
            }
        }

        // Its assembly, taken from the type rather than named by a string:
        // a discarded typeof is optimised away and loads nothing, and the
        // page then fails to compile against its own base class.
        var pageAssembly = typeof(VbPage).Assembly;

        var locations = AppDomain.CurrentDomain.GetAssemblies()
            .Append(pageAssembly)
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location);

        // Everything the runtime shipped, not only what is loaded: an
        // assembly nothing has touched yet is absent from the domain, and a
        // page using it fails with "reference required to assembly
        // Microsoft.AspNetCore.Http.Features" — a name whose absence the
        // author has no way to predict.
        //
        // TRUSTED_PLATFORM_ASSEMBLIES is how the host lists them, and it
        // covers the framework and every package the site restored.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            locations = locations.Concat(
                trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        return
        [
            .. locations
                .Where(File.Exists)
                .DistinctBy(location => Path.GetFileNameWithoutExtension(location))
                .Select(location => MetadataReference.CreateFromFile(location))
        ];
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        ForgetAll();
    }
}
