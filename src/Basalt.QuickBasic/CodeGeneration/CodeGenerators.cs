namespace Basalt.QuickBasic.CodeGeneration;

/// <summary>
/// The targets a QuickBASIC program can be built for.
///
/// One place, so that the project properties, the command line and the tests
/// all offer the same list.
/// </summary>
public static class CodeGenerators
{
    /// <summary>Every target, complete or not.</summary>
    public static IReadOnlyList<ICodeGenerator> All { get; } =
    [
        new CCodeGenerator(),
        new VisualBasicCodeGenerator(),
        new NativeCodeGenerator()
    ];

    /// <summary>The target that is used when none is asked for.</summary>
    public static ICodeGenerator Default => All[0];

    /// <summary>
    /// The target with an id, or null when there is none.
    ///
    /// Null rather than a fallback: a build asked for a target the user named,
    /// and quietly building a different one is worse than saying no.
    /// </summary>
    public static ICodeGenerator? ById(string? id) =>
        id is null
            ? null
            : All.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The targets that can actually be built.</summary>
    public static IReadOnlyList<ICodeGenerator> Buildable =>
        [.. All.Where(g => g.IsComplete)];
}
