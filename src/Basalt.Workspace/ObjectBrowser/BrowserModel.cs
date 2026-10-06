using SymbolKind = Basalt.Extensibility.SymbolKind;
using Microsoft.CodeAnalysis;

namespace Basalt.Workspace.ObjectBrowser;

/// <summary>What a library entry stands for.</summary>
public enum BrowserLibraryKind
{
    /// <summary>Everything the solution defines and references.</summary>
    All,

    /// <summary>A project of the solution: its own source.</summary>
    Project,

    /// <summary>An assembly the projects reference.</summary>
    Assembly
}

/// <summary>A project of the solution with its compilation, as the browser reads it.</summary>
public sealed class BrowserProject
{
    private readonly Func<CancellationToken, Task<Compilation?>> _compilation;

    /// <summary>A project whose compilation is already in hand.</summary>
    public BrowserProject(string name, Compilation compilation)
        : this(name, compilation.AssemblyName ?? name, _ => Task.FromResult<Compilation?>(compilation))
    {
    }

    /// <summary>
    /// A project whose compilation is made only when asked for, so that
    /// whoever needs one project does not pay for all of them.
    /// </summary>
    public BrowserProject(string name, string assemblyName, Func<CancellationToken, Task<Compilation?>> compilation)
    {
        Name = name;
        AssemblyName = assemblyName;
        _compilation = compilation;
    }

    public string Name { get; }

    /// <summary>The name of the assembly the project produces, known without compiling.</summary>
    public string AssemblyName { get; }

    public Task<Compilation?> GetCompilationAsync(CancellationToken ct) => _compilation(ct);
}

/// <summary>
/// One entry of the library selector. The assemblies stay behind the entry:
/// the panel only needs to hand it back to the service.
/// </summary>
public sealed class BrowserLibrary
{
    internal BrowserLibrary(
        string name, BrowserLibraryKind kind, IReadOnlyList<IAssemblySymbol> assemblies)
    {
        Name = name;
        Kind = kind;
        Assemblies = assemblies;
    }

    /// <summary>Empty for <see cref="BrowserLibraryKind.All"/>, which the panel names itself.</summary>
    public string Name { get; }

    public BrowserLibraryKind Kind { get; }

    internal IReadOnlyList<IAssemblySymbol> Assemblies { get; }
}

/// <summary>
/// A type or a member in the browser, with everything a list row needs
/// already worked out and the Roslyn symbol kept behind for later questions.
/// </summary>
public sealed class BrowserSymbol
{
    internal BrowserSymbol(ISymbol symbol, SymbolKind kind, string signature, bool isInherited)
    {
        Symbol = symbol;
        Kind = kind;
        Signature = signature;
        IsInherited = isInherited;

        Name = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
            ? "New"
            : symbol.ToDisplayString(NameOnly);

        Namespace = (symbol as INamedTypeSymbol ?? symbol.ContainingType as INamedTypeSymbol) is { } type
            ? type.ContainingNamespace?.ToDisplayString() ?? ""
            : symbol.ContainingNamespace?.ToDisplayString() ?? "";

        AssemblyName = symbol.ContainingAssembly?.Name ?? "";
        DeclaringType = symbol is INamedTypeSymbol { ContainingType: null }
            ? null
            : symbol.ContainingType?.ToDisplayString(ObjectBrowserService.TypeNameFormat);

        Source = SourceOf(symbol);
    }

    /// <summary>
    /// Where a symbol is declared in the user's source, as the editor should
    /// open it.
    ///
    /// A partial class has several declarations and a form's designer file is
    /// the first one the compiler lists: the author's own file is preferred,
    /// since it is the one they came to read. Lines are the mapped ones, so a
    /// declaration in generated code (a Razor view) lands in the template
    /// that produced it rather than in a file the user never opens.
    /// </summary>
    internal static BrowserSourceTarget? SourceOf(ISymbol symbol)
    {
        var locations = symbol.Locations.Where(location => location.IsInSource).ToList();

        var chosen = locations.FirstOrDefault(location => !IsGeneratedFile(location.SourceTree?.FilePath))
                     ?? locations.FirstOrDefault();

        if (chosen is null) return null;

        var span = chosen.GetMappedLineSpan();
        if (!span.IsValid || span.Path.Length == 0) return null;

        // A #ExternalSource mapping carries a line, not a column: the column of
        // the generated code means nothing in the template, so the line starts.
        var remapped = span.HasMappedPath
            && !string.Equals(span.Path, chosen.SourceTree?.FilePath, StringComparison.Ordinal);

        return new BrowserSourceTarget(
            span.Path, span.StartLinePosition.Line + 1, remapped ? 1 : span.StartLinePosition.Character + 1);
    }

    private static bool IsGeneratedFile(string? path) =>
        path is not null
        && (path.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase));

    private static readonly SymbolDisplayFormat NameOnly = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.None);

    /// <summary>The name as a list shows it: <c>List(Of T)</c>, <c>Show</c>, <c>New</c>.</summary>
    public string Name { get; }

    public SymbolKind Kind { get; }

    /// <summary>The declaration in the language of the project it comes from, VB for a VB project.</summary>
    public string Signature { get; }

    /// <summary>The namespace of the type, or of the type that holds the member.</summary>
    public string Namespace { get; }

    public string AssemblyName { get; }

    /// <summary>The type that declares a member, or the type a nested type sits in; null for a top-level type.</summary>
    public string? DeclaringType { get; }

    /// <summary>True for a member that comes from a base type or an implemented interface.</summary>
    public bool IsInherited { get; }

    public bool IsType => Symbol is INamedTypeSymbol;

    /// <summary>Where the declaration is written, or null when it comes from a referenced assembly.</summary>
    public BrowserSourceTarget? Source { get; }

    internal ISymbol Symbol { get; }
}

/// <summary>A declaration in the user's own source: file and one-based line and column.</summary>
public sealed record BrowserSourceTarget(string FilePath, int Line, int Column);

/// <summary>The text of the description pane.</summary>
public sealed record BrowserDescription(
    string Signature,
    string? DeclaringType,
    string Namespace,
    string AssemblyName,
    string? Summary,
    BrowserSourceTarget? Source);

/// <summary>
/// The types a search found, at most <see cref="Cap"/> of them, with how many
/// there were: a list that silently stops is a list that looks complete.
/// </summary>
public sealed record BrowserSearchResult(IReadOnlyList<BrowserSymbol> Types, int Total)
{
    public const int Cap = 1000;

    public bool IsTruncated => Total > Types.Count;
}
