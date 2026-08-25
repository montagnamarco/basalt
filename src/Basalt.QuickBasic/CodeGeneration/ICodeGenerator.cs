namespace Basalt.QuickBasic.CodeGeneration;

/// <summary>
/// One way of turning a QuickBASIC program into something else.
///
/// There was one output and it was C. A target is now a choice, because the
/// same program is worth having as a native binary, as VB.NET that a .NET
/// project can hold, and eventually as machine code without a C compiler in
/// between.
/// </summary>
public interface ICodeGenerator
{
    /// <summary>What to call this target, in settings and on the command line.</summary>
    string Id { get; }

    /// <summary>What to call it where a person reads it.</summary>
    string DisplayName { get; }

    /// <summary>The extension the generated file takes, with its dot.</summary>
    string FileExtension { get; }

    /// <summary>
    /// Whether this target produces something that can be built and run.
    ///
    /// False for a skeleton, so the IDE can offer it without claiming it
    /// works.
    /// </summary>
    bool IsComplete { get; }

    /// <summary>Turns a program into the target's own text.</summary>
    string Generate(Program program, SymbolTable symbols);
}

/// <summary>
/// The C target, which is what the compiler has always produced.
///
/// A thin cover over the existing writer rather than a rewrite of it: the C
/// backend works and is tested, and turning it into a target should not have
/// changed what it emits.
/// </summary>
public sealed class CCodeGenerator : ICodeGenerator
{
    public string Id => "c";

    public string DisplayName => "C (native, through clang)";

    public string FileExtension => ".c";

    public bool IsComplete => true;

    public string Generate(Program program, SymbolTable symbols) =>
        CodeWriter.Generate(program, symbols);
}
