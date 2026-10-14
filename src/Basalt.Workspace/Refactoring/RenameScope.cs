using Microsoft.CodeAnalysis;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// The solution a rename is worked out in, when the solution holds C# projects Basalt cannot compile.
/// </summary>
/// <remarks>
/// The workspace loads Visual Basic language services only. Asking Roslyn for the compilation of a C# project fails
/// with "compilation is required, but it is not supported by project ...", and the renamer compiles more than the
/// projects that use the symbol: a fresh WinForms project references the C# Basalt.Forms, and every rename in it
/// failed. A C# project can use a symbol only by depending on the project that declares it, so the C# projects that do
/// not are left out, and the rename runs without them. A C# project that does depend on the declaring project refuses
/// the rename only when it can reach the symbol: a public, protected or protected friend member of the project, or an
/// internal one that InternalsVisibleTo hands to it. Locals, parameters and private members are never visible outside
/// their project, so renaming them is not blocked by a C# project that merely references it.
/// </remarks>
internal static class RenameScope
{
    /// <summary>The solution to rename in, and the C# project that blocks the rename when there is one.</summary>
    public readonly record struct Scope(Solution Solution, string? BlockingProject);

    public static Scope For(Solution solution, ISymbol symbol)
    {
        var others = solution.Projects.Where(project => project.Language != LanguageNames.VisualBasic).ToArray();
        if (others.Length == 0) return new Scope(solution, null);

        var declaring = symbol.Locations
            .Where(location => location.IsInSource && location.SourceTree is not null)
            .Select(location => solution.GetDocument(location.SourceTree!)?.Project.Id)
            .OfType<ProjectId>()
            .ToHashSet();
        var graph = solution.GetProjectDependencyGraph();

        foreach (var project in others)
        {
            if (!graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id).Any(declaring.Contains)) continue;

            if (CanBeReachedFrom(symbol, project.AssemblyName))
                return new Scope(solution, project.Name);
        }

        var scoped = solution;
        foreach (var project in others) scoped = scoped.RemoveProject(project.Id);
        return new Scope(scoped, null);
    }

    /// <summary>
    /// Whether code in another assembly, named <paramref name="assemblyName"/>, can name the symbol.
    /// </summary>
    /// <remarks>
    /// Walks from the symbol to its namespace. A private member, or a local or parameter, anywhere on the way makes the
    /// symbol invisible outside the project. When every level is public, protected or protected friend, it is visible.
    /// An internal (Friend) level is visible only to an assembly InternalsVisibleTo names.
    /// </remarks>
    private static bool CanBeReachedFrom(ISymbol symbol, string assemblyName)
    {
        var internalLevel = false;

        for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            switch (current.DeclaredAccessibility)
            {
                // Protected Friend is reachable from any subclass, a C# one included.
                case Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal:
                    break;

                // Private Protected is a Friend level too: a subclass in an assembly InternalsVisibleTo names reaches it.
                case Accessibility.Internal or Accessibility.ProtectedAndInternal:
                    internalLevel = true;
                    break;

                default:
                    // Private, or NotApplicable: a local, a parameter, a label. Nothing outside can name it.
                    return false;
            }
        }

        return !internalLevel || GivesAccessTo(symbol.ContainingAssembly, assemblyName);
    }

    /// <summary>Whether the assembly names <paramref name="assemblyName"/> in an InternalsVisibleTo attribute.</summary>
    private static bool GivesAccessTo(IAssemblySymbol? assembly, string assemblyName)
    {
        if (assembly is null) return false;

        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                continue;

            // The argument is the friend's name, optionally followed by ", PublicKey=...".
            var friend = (attribute.ConstructorArguments.FirstOrDefault().Value as string)?.Split(',')[0].Trim();

            if (string.Equals(friend, assemblyName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
