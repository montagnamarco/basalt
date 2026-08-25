using System.Text;

namespace Basalt.QuickBasic.CodeGeneration;

/// <summary>
/// An instruction of the intermediate representation.
///
/// Deliberately small and close to a machine: three-address, one operation
/// each, no nesting. Going from the tree straight to arm64 would mean solving
/// register allocation and control flow at once, which is how a backend ends
/// up unfinishable.
/// </summary>
/// <param name="Op">What it does.</param>
/// <param name="Target">Where the result goes: a temporary or a variable.</param>
/// <param name="Left">The first operand, or the only one.</param>
/// <param name="Right">The second operand, where there is one.</param>
public sealed record IrInstruction(
    IrOp Op,
    string? Target = null,
    string? Left = null,
    string? Right = null)
{
    public override string ToString() => Op switch
    {
        IrOp.Label => $"{Left}:",
        IrOp.Jump => $"    jmp {Left}",
        IrOp.JumpIfZero => $"    jz {Left}, {Right}",
        IrOp.Load => $"    {Target} = {Left}",
        IrOp.Print => $"    print {Left}",
        IrOp.Return => $"    ret {Left}",
        _ => $"    {Target} = {Left} {Symbol(Op)} {Right}"
    };

    private static string Symbol(IrOp op) => op switch
    {
        IrOp.Add => "+", IrOp.Subtract => "-", IrOp.Multiply => "*",
        IrOp.Divide => "/", IrOp.Compare => "?",
        _ => "?"
    };
}

/// <summary>What an instruction does.</summary>
public enum IrOp
{
    Load, Add, Subtract, Multiply, Divide, Compare,
    Jump, JumpIfZero, Label, Print, Return
}

/// <summary>
/// The beginning of a native backend that needs no C compiler.
///
/// **This is a skeleton and does not produce a working program.** It exists to
/// fix the shape of the thing — a small intermediate representation, then
/// instruction selection per architecture — so that the work is a matter of
/// filling in rather than deciding.
///
/// What is here:
///   - the intermediate representation above, and a lowering of arithmetic,
///     PRINT and assignment onto it;
///   - a sketch of arm64 selection for those instructions.
///
/// What is not, and what each needs:
///   - **Register allocation.** Every temporary currently becomes a stack
///     slot. Real allocation wants live ranges and a linear scan over them.
///   - **Calls.** Nothing implements the platform calling convention: on
///     arm64 that means x0-x7 for arguments, x29/x30 for the frame, and
///     16-byte stack alignment.
///   - **Strings.** The C backend has a length-and-pointer type; this has no
///     runtime at all, so a program that touches a string cannot be lowered.
///   - **Floating point.** Only whole numbers are lowered, and QuickBASIC's
///     default type is SINGLE, so almost every real program needs the v
///     registers first.
///   - **Object files.** There is no assembler and no linker here, so even a
///     complete selection would produce text and stop.
///
/// Until those exist the target says so through <see cref="IsComplete"/>,
/// which the IDE reads: offering it is honest, pretending it builds is not.
/// </summary>
public sealed class NativeCodeGenerator : ICodeGenerator
{
    public string Id => "native";

    public string DisplayName => "Native (skeleton, does not build yet)";

    public string FileExtension => ".s";

    /// <summary>
    /// False, and it matters.
    ///
    /// The IDE offers this target and refuses to build with it, rather than
    /// producing an object file that will not link.
    /// </summary>
    public bool IsComplete => false;

    public string Generate(Program program, SymbolTable symbols)
    {
        var instructions = Lower(program);

        var output = new StringBuilder();

        output.Append("// QuickBASIC, lowered to the intermediate representation.\n");
        output.Append("// This is a skeleton: see NativeCodeGenerator for what is missing.\n\n");

        foreach (var instruction in instructions)
            output.Append(instruction).Append('\n');

        output.Append("\n// A sketch of arm64 selection for the above.\n");

        foreach (var line in SelectArm64(instructions))
            output.Append(line).Append('\n');

        return output.ToString();
    }

    /// <summary>
    /// Turns the tree into the intermediate representation.
    ///
    /// Only the parts that lower cleanly today: arithmetic on whole numbers,
    /// assignment, and PRINT. Anything else is passed over rather than
    /// half-lowered, since a wrong lowering is harder to find than a missing
    /// one.
    /// </summary>
    public IReadOnlyList<IrInstruction> Lower(Program program)
    {
        var instructions = new List<IrInstruction>();
        var next = 0;

        string Temporary() => $"t{next++}";

        string? Value(Expression expression)
        {
            switch (expression)
            {
                case NumberLiteral number when number.Value == Math.Floor(number.Value):
                    var slot = Temporary();
                    instructions.Add(new IrInstruction(
                        IrOp.Load, slot, ((long)number.Value).ToString()));
                    return slot;

                case VariableReference variable:
                    return variable.Name;

                case Binary binary:
                    var op = binary.Operator switch
                    {
                        "+" => IrOp.Add,
                        "-" => IrOp.Subtract,
                        "*" => IrOp.Multiply,
                        "/" => IrOp.Divide,
                        _ => (IrOp?)null
                    };

                    if (op is null) return null;

                    var left = Value(binary.Left);
                    var right = Value(binary.Right);

                    if (left is null || right is null) return null;

                    var result = Temporary();

                    instructions.Add(new IrInstruction(op.Value, result, left, right));

                    return result;

                default:
                    return null;
            }
        }

        foreach (var statement in program.Main)
        {
            switch (statement)
            {
                case Assignment { Target: VariableReference target } assignment:
                    if (Value(assignment.Value) is { } value)
                        instructions.Add(new IrInstruction(IrOp.Load, target.Name, value));
                    break;

                case PrintStatement print:
                    foreach (var item in print.Values)
                        if (Value(item) is { } printed)
                            instructions.Add(new IrInstruction(IrOp.Print, Left: printed));
                    break;
            }
        }

        instructions.Add(new IrInstruction(IrOp.Return, Left: "0"));

        return instructions;
    }

    /// <summary>
    /// A sketch of arm64 instruction selection.
    ///
    /// One instruction at a time onto one or two arm64 instructions, with
    /// every value living in a stack slot because there is no register
    /// allocator. The shape is right; the output does not assemble.
    /// </summary>
    public IReadOnlyList<string> SelectArm64(IReadOnlyList<IrInstruction> instructions)
    {
        var lines = new List<string> { "// _main:" };

        foreach (var instruction in instructions)
        {
            lines.Add(instruction.Op switch
            {
                // A literal wants mov for small values and a movz/movk pair
                // beyond 16 bits; only the small case is written.
                IrOp.Load when long.TryParse(instruction.Left, out var number) =>
                    $"//   mov x0, #{number}          ; -> {instruction.Target}",

                IrOp.Load =>
                    $"//   ldr x0, [sp, #{instruction.Left}]  ; -> {instruction.Target}",

                IrOp.Add => $"//   add x0, x1, x2         ; {instruction.Target}",
                IrOp.Subtract => $"//   sub x0, x1, x2         ; {instruction.Target}",
                IrOp.Multiply => $"//   mul x0, x1, x2         ; {instruction.Target}",

                // sdiv is signed division; QuickBASIC's / is real division,
                // so this is only right for the integer case.
                IrOp.Divide => $"//   sdiv x0, x1, x2        ; {instruction.Target}",

                // Needs the calling convention, which is not written.
                IrOp.Print => "//   bl _qb_print_number     ; needs the ABI",

                IrOp.Return => "//   ret",

                _ => $"//   ; {instruction.Op} is not selected yet"
            });
        }

        return lines;
    }
}
