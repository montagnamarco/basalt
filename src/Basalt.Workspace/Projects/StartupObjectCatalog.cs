using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Projects;

/// <summary>
/// What the Startup object list of a project offers: the types that can start it, and the forms among them.
/// </summary>
/// <param name="RootNamespace">The root namespace the compiler gave the names, the project's own or its name when it sets none.</param>
/// <param name="Names">Every type that can start the program, fully qualified with <paramref name="RootNamespace"/>.</param>
/// <param name="Forms">The Windows Forms forms among them, fully qualified in the same way.</param>
public sealed record StartupObjects(string RootNamespace, IReadOnlyList<string> Names, IReadOnlyList<string> Forms);

/// <summary>
/// The types a project can start from, for the Startup object list of the
/// properties window: every module, class or structure with a Visual Basic
/// entry point (<c>Sub Main</c> or <c>Function Main</c>, Shared in a class or a
/// structure), and, in a Windows Forms project, every form.
/// </summary>
/// <remarks>
/// Answered by the compiler rather than by reading the text. Whether a class is
/// a form is its base class, which only the compiler resolves: a form may
/// inherit through another class library. The compilation is built on the
/// thread pool; the caller awaits it on the interface thread.
/// </remarks>
public static class StartupObjectCatalog
{
    private const string FormName = "Form";
    private const string FormNamespace = "System.Windows.Forms";

    /// <summary>
    /// The names as <c>StartupObject</c> records them: fully qualified, with
    /// the project's RootNamespace in front, sorted without regard to case.
    /// </summary>
    /// <exception cref="Exception">The project could not be loaded; the caller reports it.</exception>
    public static async Task<IReadOnlyList<string>> FindAsync(string projectPath, CancellationToken ct = default) =>
        (await ReadAsync(projectPath, ct).ConfigureAwait(false)).Names;

    /// <summary>The names and the forms, and the root namespace the compiler gave them.</summary>
    public static Task<StartupObjects> ReadAsync(string projectPath, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var compilation = await CompilationOfAsync(projectPath, ct).ConfigureAwait(false);
            var root = (compilation.Options as VisualBasicCompilationOptions)?.RootNamespace ?? "";
            var (names, forms) = Startable(compilation, ct);
            return new StartupObjects(root, names, forms);
        }, ct);

    private static async Task<Compilation> CompilationOfAsync(string projectPath, CancellationToken ct)
    {
        // The properties the build and the language service use, so that a
        // Windows desktop project is read as it is retargeted to run.
        var retargeting = WindowsDesktopProjectCompatibility.Resolve(projectPath);
        var properties = retargeting.Properties.ToDictionary(
            p => p.Key, p => WindowsDesktopProjectCompatibility.Escape(p.Value));

        MsBuildEnvironment.EnsureInitialized();

        using var workspace = MSBuildWorkspace.Create(properties);
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct).ConfigureAwait(false);

        return await project.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The project has no compilation.");
    }

    private static (IReadOnlyList<string> Names, IReadOnlyList<string> Forms) Startable(
        Compilation compilation, CancellationToken ct)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var forms = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // A partial class has a declaration in each of its files, and all of them name one symbol.
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(tree);
            foreach (var block in tree.GetRoot(ct).DescendantNodes().OfType<TypeBlockSyntax>())
            {
                if (model.GetDeclaredSymbol(block, ct) is not INamedTypeSymbol type || !seen.Add(type)) continue;

                var name = type.ToDisplayString(QualifiedName);
                if (IsForm(type)) forms.Add(name);
                if (IsStartable(type)) names.Add(name);
            }
        }

        return ([.. names], [.. forms]);
    }

    /// <summary>Fully qualified, dot separated, and without the global namespace, which has no name to write.</summary>
    private static readonly SymbolDisplayFormat QualifiedName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private static bool IsStartable(INamedTypeSymbol type)
    {
        // The runtime starts a type that is not generic: there is no type argument to give it.
        if (type.TypeParameters.Length > 0) return false;

        return type.TypeKind switch
        {
            // A module's members are shared already; a class or a structure needs Shared on its entry point.
            TypeKind.Module or TypeKind.Class or TypeKind.Structure =>
                type.GetMembers("Main").OfType<IMethodSymbol>().Any(IsEntryPoint)
                || IsForm(type),
            _ => false
        };
    }

    /// <summary>
    /// A shared Sub or Function named Main, with no parameters or with one String array, as Visual Basic accepts
    /// for the program's entry point. A Sub or a Function returning Integer are both entry points.
    /// </summary>
    private static bool IsEntryPoint(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary
        && method.IsStatic
        && (method.ReturnsVoid || method.ReturnType.SpecialType == SpecialType.System_Int32)
        && method.Parameters.Length switch
        {
            0 => true,
            1 => method.Parameters[0].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String },
            _ => false
        };

    /// <summary>A class that derives from the Windows Forms Form and can be created.</summary>
    private static bool IsForm(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class && !type.IsAbstract && DerivesFromForm(type);

    private static bool DerivesFromForm(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.Name == FormName
                && baseType.ContainingNamespace?.ToDisplayString() == FormNamespace)
                return true;
        }

        return false;
    }
}
