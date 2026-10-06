using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SymbolKind = Basalt.Extensibility.SymbolKind;
using Microsoft.CodeAnalysis;

namespace Basalt.Workspace.ObjectBrowser;

/// <summary>
/// What the Object Browser asks the compiler: libraries, namespaces, types,
/// members and descriptions.
///
/// Every question is answered on a pool thread. A referenced assembly such as
/// System.Private.CoreLib decodes its metadata on first touch, which is long
/// enough to be felt on the interface thread, so nothing here is ever
/// synchronous with the caller. The tree is also read lazily — a namespace
/// lists its types only when it is opened, a type its members only when it is
/// selected — because decoding every member of every referenced type to draw
/// a list of names would make the first F-key press a wait.
/// </summary>
public sealed class ObjectBrowserService
{
    /// <summary>
    /// Visual Basic's own spelling of a declaration: access, modifiers, the
    /// <c>Sub</c>/<c>Function</c>/<c>Property</c> keyword, parameters with
    /// <c>As</c>. Roslyn prints it in the language of the compilation the
    /// symbol came from, so a VB project reads as <c>Public Sub Show(text As String)</c>.
    /// </summary>
    internal static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeVariance
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        parameterOptions: SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers,
        kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword
            | SymbolDisplayKindOptions.IncludeMemberKeyword);

    /// <summary>
    /// The declaration of a type that has a keyword of its own (String,
    /// Integer...). Without the special-type shorthand it would be described
    /// as just <c>String</c> instead of as the class it is, and without
    /// escaping it reads <c>String</c> and not <c>[String]</c>.
    /// </summary>
    internal static readonly SymbolDisplayFormat TypeDeclarationFormat = SignatureFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None);

    /// <summary>How a containing type is named in the description.</summary>
    internal static readonly SymbolDisplayFormat TypeNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly Func<CancellationToken, Task<IReadOnlyList<BrowserProject>>> _projects;
    private readonly Func<string, bool> _pathExists;

    /// <param name="projects">The compilations to browse, asked for on a pool thread.</param>
    /// <param name="pathExists">Whether a source path can be opened; false for a path only a generator knew.</param>
    public ObjectBrowserService(
        Func<CancellationToken, Task<IReadOnlyList<BrowserProject>>> projects,
        Func<string, bool>? pathExists = null)
    {
        _projects = projects;
        _pathExists = pathExists ?? File.Exists;
    }

    /// <summary>Browses what the language service holds, one compilation per project.</summary>
    public ObjectBrowserService(RoslynLanguageService language)
        : this(ct => CompilationsOf(language.CurrentSolution, ct))
    {
    }

    private static async Task<IReadOnlyList<BrowserProject>> CompilationsOf(
        Solution? solution, CancellationToken ct)
    {
        var projects = new List<BrowserProject>();

        foreach (var project in solution?.Projects ?? [])
        {

            projects.Add(new BrowserProject(project.Name, project.AssemblyName, project.GetCompilationAsync));
        }

        return projects;
    }

    private static async Task<IReadOnlyList<(string Name, IAssemblySymbol Assembly, IModuleSymbol SourceModule)>> CompiledAsync(
        IReadOnlyList<BrowserProject> projects, CancellationToken ct)
    {
        var compiled = new List<(string, IAssemblySymbol, IModuleSymbol)>();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is { } compilation)
                compiled.Add((project.Name, compilation.Assembly, compilation.SourceModule));
        }

        return compiled;
    }

    /// <summary>
    /// "All", then each project, then the assemblies the projects reference
    /// (each once, even when several projects use it).
    /// </summary>
    public Task<IReadOnlyList<BrowserLibrary>> GetLibrariesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<BrowserLibrary>>(async () =>
        {
            var projects = await CompiledAsync(await _projects(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            var own = projects.Select(project => project.Assembly).ToList();

            // A project that another project references shows up among the
            // referenced assemblies too, as a second copy of itself: it is
            // listed once, as the project it is.
            var ownNames = own.Select(assembly => assembly.Identity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var referenced = projects
                .SelectMany(project => project.SourceModule.ReferencedAssemblySymbols)
                .Where(assembly => !ownNames.Contains(assembly.Identity.Name))
                .GroupBy(assembly => assembly.Identity.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(assembly => assembly.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var libraries = new List<BrowserLibrary>
            {
                new("", BrowserLibraryKind.All, [.. own, .. referenced])
            };

            for (var index = 0; index < projects.Count; index++)
                libraries.Add(new BrowserLibrary(
                    projects[index].Name, BrowserLibraryKind.Project, [own[index]]));

            libraries.AddRange(referenced.Select(assembly => new BrowserLibrary(
                assembly.Name, BrowserLibraryKind.Assembly, [assembly])));

            return libraries;
        }, ct);

    /// <summary>The namespaces of a library that declare at least one type of their own.</summary>
    public Task<IReadOnlyList<string>> GetNamespacesAsync(
        BrowserLibrary library, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assembly in library.Assemblies)
                CollectNamespaces(assembly.GlobalNamespace, FromSource(assembly), names, ct);

            return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    private static void CollectNamespaces(
        INamespaceSymbol ns, bool fromSource, HashSet<string> names, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ns.GetTypeMembers().Any(type => IsBrowsable(type, fromSource)))
            names.Add(ns.ToDisplayString());

        foreach (var child in ns.GetNamespaceMembers())
            CollectNamespaces(child, fromSource, names, ct);
    }

    /// <summary>The types a namespace declares directly, across the library's assemblies.</summary>
    public Task<IReadOnlyList<BrowserSymbol>> GetTypesAsync(
        BrowserLibrary library, string namespaceName, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<BrowserSymbol>>(() =>
        {
            var types = new List<BrowserSymbol>();

            foreach (var assembly in library.Assemblies)
            {
                ct.ThrowIfCancellationRequested();

                var ns = FindNamespace(assembly.GlobalNamespace, namespaceName);
                if (ns is null) continue;

                var fromSource = FromSource(assembly);

                types.AddRange(ns.GetTypeMembers()
                    .Where(type => IsBrowsable(type, fromSource))
                    .Select(ToBrowserSymbol));
            }

            return types
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    /// <summary>
    /// The types whose name contains the query, and those with a member whose
    /// name does. Sorted before it is cut at <see cref="BrowserSearchResult.Cap"/>,
    /// so the ones kept are the first by name and not the first found, and the
    /// total says how many there were.
    /// </summary>
    public Task<BrowserSearchResult> SearchAsync(
        BrowserLibrary library, string query, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var found = new List<INamedTypeSymbol>();
            if (string.IsNullOrWhiteSpace(query)) return new BrowserSearchResult([], 0);

            var needle = query.Trim();

            foreach (var assembly in library.Assemblies)
                SearchNamespace(assembly.GlobalNamespace, FromSource(assembly), needle, found, ct);

            // Signatures are worked out for the types kept, not for every hit.
            var kept = found
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(type => type.Arity)
                .ThenBy(type => type.ContainingNamespace?.ToDisplayString(), StringComparer.Ordinal)
                .Take(BrowserSearchResult.Cap)
                .Select(ToBrowserSymbol)
                .ToList();

            return new BrowserSearchResult(kept, found.Count);
        }, ct);

    private static void SearchNamespace(
        INamespaceSymbol ns, bool fromSource, string needle, List<INamedTypeSymbol> found, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsBrowsable(type, fromSource)) continue;

            if (Matches(type.Name, needle)
                || type.GetMembers().Any(member => IsBrowsableMember(member, fromSource) && Matches(member.Name, needle)))
                found.Add(type);
        }

        foreach (var child in ns.GetNamespaceMembers())
            SearchNamespace(child, fromSource, needle, found, ct);
    }

    private static bool Matches(string name, string needle) =>
        name.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The members of a type, sorted by name. With <paramref name="includeInherited"/>
    /// the members of its base types (or of the interfaces it extends) follow,
    /// except the ones a more derived type overrides or hides.
    /// </summary>
    public Task<IReadOnlyList<BrowserSymbol>> GetMembersAsync(
        BrowserSymbol type, bool includeInherited, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<BrowserSymbol>>(() =>
        {
            var members = new List<BrowserSymbol>();

            if (type.Symbol is not INamedTypeSymbol named) return members;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            AddMembers(named, isInherited: false, members, seen, ct);

            if (includeInherited)
            {
                IEnumerable<INamedTypeSymbol> ancestors = named.TypeKind == TypeKind.Interface
                    ? named.AllInterfaces
                    : Ancestors(named);

                foreach (var ancestor in ancestors)
                    AddMembers(ancestor, isInherited: true, members, seen, ct);
            }

            return members
                .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.Signature, StringComparer.Ordinal)
                .ToList();
        }, ct);

    private static IEnumerable<INamedTypeSymbol> Ancestors(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            yield return baseType;
    }

    private static void AddMembers(
        INamedTypeSymbol owner, bool isInherited, List<BrowserSymbol> into,
        HashSet<string> seen, CancellationToken ct)
    {
        var fromSource = FromSource(owner.ContainingAssembly);

        foreach (var member in owner.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (!IsBrowsableMember(member, fromSource)) continue;

            // Constructors are not inherited: a base class's New is not a way
            // to make the derived type.
            if (isInherited && member is IMethodSymbol { MethodKind: MethodKind.Constructor }) continue;

            // A member of a base type that a derived type overrides or hides has
            // the same key: it is listed once, as the derived one. A type's
            // own members are all listed — two of them with a look-alike key
            // are overloads — but their keys are noted so the base's go away.
            var isFirst = seen.Add(KeyOf(member));
            if (isInherited && !isFirst) continue;

            into.Add(ToBrowserSymbol(member, isInherited));
        }
    }

    /// <summary>
    /// What makes two members the same member: name, arity, parameter types
    /// with how they are passed, and the return type. The return type matters
    /// for conversion operators, which differ by nothing else.
    /// </summary>
    private static readonly SymbolDisplayFormat SeenFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static string KeyOf(ISymbol member)
    {
        var arity = member is IMethodSymbol method ? method.Arity : 0;
        return $"{member.Kind}:{arity}:{member.ToDisplayString(SeenFormat)}";
    }

    /// <summary>
    /// The members of <paramref name="members"/> that belong in the list for a
    /// search: all of them when the type itself matched, else the ones that
    /// match by name.
    /// </summary>
    public static IReadOnlyList<BrowserSymbol> FilterMembers(
        BrowserSymbol type, IReadOnlyList<BrowserSymbol> members, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return members;

        var needle = query.Trim();
        if (Matches(type.Name, needle)) return members;

        return members.Where(member => Matches(member.Name, needle)).ToList();
    }

    /// <summary>
    /// Where a symbol is declared now. The browser's symbols belong to the
    /// compilations read when it was loaded; after an edit their lines are
    /// the old ones, and opening the file there lands on the wrong line.
    /// The symbol is found again by its documentation id in the current
    /// compilation of the project that declares it. Null when it is no longer
    /// there, is not declared in source, or its file cannot be opened.
    /// </summary>
    public Task<BrowserSourceTarget?> ResolveSourceAsync(BrowserSymbol item, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            if (item.Source is null) return null;

            var id = item.Symbol.GetDocumentationCommentId();
            var assemblyName = item.Symbol.ContainingAssembly?.Name;
            if (id is null || assemblyName is null) return Existing(item.Source);

            var projects = await _projects(ct).ConfigureAwait(false);

            // Only the project that declares the symbol is compiled: asking
            // every project for its compilation to look in one would turn a
            // double-click into a build of the solution.
            foreach (var project in projects.Where(p => string.Equals(p.AssemblyName, assemblyName, StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();

                if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is not { } compilation) continue;

                var current = DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation);
                if (current is null || current.ContainingAssembly?.Name != assemblyName) continue;

                return Existing(BrowserSymbol.SourceOf(current));
            }

            return null;
        }, ct);

    private BrowserSourceTarget? Existing(BrowserSourceTarget? target) =>
        target is not null && _pathExists(target.FilePath) ? target : null;

    /// <summary>The text of the description pane, with the XML summary when the compiler can read one.</summary>
    public Task<BrowserDescription> DescribeAsync(BrowserSymbol item, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var xml = item.Symbol.GetDocumentationCommentXml(cancellationToken: ct);

            return new BrowserDescription(
                item.Signature,
                item.DeclaringType,
                item.Namespace,
                item.AssemblyName,
                SummaryOf(xml),
                item.Source);
        }, ct);

    /// <summary>
    /// The plain text of the &lt;summary&gt; of a documentation comment.
    ///
    /// Read as XML rather than cut out between two strings: a summary holds
    /// <c>&lt;see cref="..."/&gt;</c> and <c>&lt;paramref/&gt;</c>, which should
    /// read as the name they point at, not vanish or show as markup.
    /// </summary>
    internal static string? SummaryOf(string? documentationXml)
    {
        if (string.IsNullOrWhiteSpace(documentationXml)) return null;

        XElement? summary;

        try
        {
            summary = XElement.Parse("<doc>" + documentationXml + "</doc>").Descendants("summary").FirstOrDefault();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (summary is null) return null;

        var text = new StringBuilder();

        foreach (var node in summary.DescendantNodes())
        {
            switch (node)
            {
                case XText plain:
                    text.Append(plain.Value);
                    break;

                case XElement { Name.LocalName: "see" or "seealso" } reference:
                    var target = (string?)reference.Attribute("cref") ?? (string?)reference.Attribute("langword");
                    if (target is not null) text.Append(LastSegment(target));
                    break;

                case XElement { Name.LocalName: "paramref" or "typeparamref" } parameter:
                    text.Append((string?)parameter.Attribute("name"));
                    break;
            }
        }

        var collapsed = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string LastSegment(string cref)
    {
        // "M:Ns.Type.Method(System.String)" -> "Method"
        var name = cref.Contains(':') ? cref[(cref.IndexOf(':') + 1)..] : cref;
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0) name = name[..parenthesis];

        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string name)
    {
        if (name.Length == 0) return root;

        var current = root;

        foreach (var part in name.Split('.'))
        {
            current = current.GetNamespaceMembers().FirstOrDefault(child => child.Name == part);
            if (current is null) return null;
        }

        return current;
    }

    private static bool FromSource(IAssemblySymbol? assembly) => assembly?.GetMetadata() is null;

    private static bool IsBrowsable(INamedTypeSymbol type, bool fromSource) =>
        IsBrowsableMember(type, fromSource);

    /// <summary>
    /// Whether a symbol belongs in the browser: what a caller could name.
    /// Private members stay out, and so does what the compiler writes on the
    /// author's behalf (backing fields, accessors, the closure types whose
    /// names start with an angle bracket). A project's own Friend members are
    /// shown, because they are the author's; a referenced assembly's are not.
    /// </summary>
    private static bool IsBrowsableMember(ISymbol member, bool fromSource)
    {
        if (member.IsImplicitlyDeclared) return false;
        if (member.Name.StartsWith('<')) return false;

        if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
            or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise
            or MethodKind.StaticConstructor or MethodKind.Destructor })
            return false;

        return member.DeclaredAccessibility switch
        {
            Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
            Accessibility.Internal or Accessibility.ProtectedAndInternal => fromSource,
            _ => false
        };
    }

    private static BrowserSymbol ToBrowserSymbol(INamedTypeSymbol type) =>
        ToBrowserSymbol(type, isInherited: false);

    private static BrowserSymbol ToBrowserSymbol(ISymbol symbol, bool isInherited = false) =>
        new(symbol, KindOf(symbol), SignatureOf(symbol), isInherited);

    /// <summary>
    /// The declaration as the language of the symbol writes it.
    ///
    /// Roslyn's Visual Basic display leaves two things for the caller.
    /// A type is shown without its access and its <c>MustInherit</c> or
    /// <c>NotInheritable</c> (those options only apply to members), so they
    /// are written here from the symbol's own properties. And a method read
    /// from metadata is shown as <c>Overloads</c>, because every .NET method
    /// is hide-by-signature; nobody writes that in a declaration, so the
    /// keyword is dropped by part, not by cutting text.
    /// </summary>
    internal static string SignatureOf(ISymbol symbol)
    {
        var isVisualBasic = symbol.Language == LanguageNames.VisualBasic;

        if (symbol is INamedTypeSymbol type)
        {
            var declaration = type.ToDisplayString(
                type.SpecialType == SpecialType.None ? SignatureFormat : TypeDeclarationFormat);
            return isVisualBasic ? TypeModifiers(type) + declaration : declaration;
        }

        var parts = symbol.ToDisplayParts(SignatureFormat);
        var text = new StringBuilder();

        for (var index = 0; index < parts.Length; index++)
        {
            // Visual Basic spells an optional parameter with Optional before
            // it (and before its ByRef). Written from the parameter symbol the
            // part stands for, never from the text: brackets also mean arrays.
            if (isVisualBasic
                && parts[index].Kind == SymbolDisplayPartKind.ParameterName
                && parts[index].Symbol is IParameterSymbol { IsOptional: true } parameter
                && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, symbol))
            {
                const string ByRef = "ByRef ";
                var insertAt = text.ToString().EndsWith(ByRef, StringComparison.Ordinal) ? text.Length - ByRef.Length : text.Length;
                text.Insert(insertAt, "Optional ");
            }

            if (parts[index].Kind == SymbolDisplayPartKind.Keyword
                && parts[index].ToString() == "Overloads")
            {
                // The space that separates it from what follows goes with it.
                if (index + 1 < parts.Length && parts[index + 1].ToString() == " ") index++;
                continue;
            }

            text.Append(parts[index]);
        }

        return text.ToString();
    }

    private static string TypeModifiers(INamedTypeSymbol type)
    {
        var access = type.DeclaredAccessibility switch
        {
            Accessibility.Public => "Public ",
            Accessibility.Internal => "Friend ",
            Accessibility.Protected => "Protected ",
            Accessibility.ProtectedOrInternal => "Protected Friend ",
            Accessibility.ProtectedAndInternal => "Private Protected ",
            Accessibility.Private => "Private ",
            _ => ""
        };

        var modifier = type.TypeKind == TypeKind.Class
            ? type.IsAbstract ? "MustInherit " : type.IsSealed ? "NotInheritable " : ""
            : "";

        return access + modifier;
    }

    private static SymbolKind KindOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => SymbolKind.Interface,
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => SymbolKind.Structure,
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => SymbolKind.Enum,
        INamedTypeSymbol { TypeKind: TypeKind.Module } => SymbolKind.Module,
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } => SymbolKind.Delegate,
        INamedTypeSymbol => SymbolKind.Class,
        IMethodSymbol { MethodKind: MethodKind.Constructor } => SymbolKind.Method,
        IMethodSymbol { ReturnsVoid: true } => SymbolKind.Method,
        IMethodSymbol => SymbolKind.Function,
        IPropertySymbol => SymbolKind.Property,
        IFieldSymbol { IsConst: true } => SymbolKind.Constant,
        IFieldSymbol => SymbolKind.Field,
        IEventSymbol => SymbolKind.Event,
        _ => SymbolKind.Unknown
    };
}
