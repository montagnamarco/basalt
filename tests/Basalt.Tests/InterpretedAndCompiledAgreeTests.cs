using System.Diagnostics;
using Basalt.Extensibility;
using Basalt.Extensibility.Interpretation;
using Basalt.QuickBasic;
using Basalt.QuickBasic.Interpretation;

namespace Basalt.Tests;

/// <summary>
/// The same program, interpreted and compiled, printing the same thing.
///
/// This is the check that matters for the interpreter: a program debugged
/// step by step and then built has to behave the same way, or the debugging
/// was of a different program than the one that ships. Everything else about
/// the interpreter is a detail next to this.
/// </summary>
public sealed class InterpretedAndCompiledAgreeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-agree", Guid.NewGuid().ToString("N"));

    public InterpretedAndCompiledAgreeTests() => Directory.CreateDirectory(_root);

    /// <summary>What the interpreter prints.</summary>
    private static async Task<string> InterpretedAsync(string source)
    {
        var interpreter = new QuickBasicInterpreter();

        var errors = interpreter.Load(source);

        Assert.True(errors.Count == 0, $"Did not read:\n{string.Join("\n", errors)}");

        var printed = new System.Text.StringBuilder();
        interpreter.Output += (_, text) => printed.Append(text);

        var stop = await interpreter.RunAsync();

        Assert.True(stop.Reason != StopReason.Error, $"Error: {stop.Message}");

        return printed.ToString();
    }

    /// <summary>What the compiled program prints.</summary>
    private async Task<string> CompiledAsync(string source, string name)
    {
        Clang.SkipUnlessInstalled();

        var path = Path.Combine(_root, $"{name}.bas");
        var binary = Path.Combine(_root, name);

        await File.WriteAllTextAsync(path, source);

        var compiled = await new QuickBasicLanguageProvider().Compiler!.CompileAsync(
            path, new CompilationTarget(HostRuntimeIdentifier) { OutputPath = binary });

        Assert.True(compiled.Succeeded,
            $"Did not compile:\n{string.Join("\n", compiled.Diagnostics.Select(d => d.Message))}");

        using var process = Process.Start(new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        return output;
    }

    /// <summary>Runs a program both ways and insists the output agrees.</summary>
    private async Task AgreeAsync(string name, string source)
    {
        var interpreted = await InterpretedAsync(source);
        var compiled = await CompiledAsync(source, name);

        Assert.Equal(compiled, interpreted);
    }

    [Fact]
    public Task OnPrintingNumbers() => AgreeAsync("numbers", """
        PRINT 1
        PRINT -1
        PRINT 42
        PRINT 0
        """);

    [Fact]
    public Task OnArithmetic() => AgreeAsync("arithmetic", """
        PRINT 1 + 2 * 3
        PRINT (1 + 2) * 3
        PRINT 7 \ 2
        PRINT 7 MOD 2
        PRINT 2 ^ 10
        """);

    [Fact]
    public Task OnStrings() => AgreeAsync("strings", """
        PRINT "hello"
        PRINT LEN("hello")
        PRINT LEFT$("abcdef", 3)
        PRINT RIGHT$("abcdef", 3)
        PRINT MID$("abcdef", 2, 3)
        PRINT UCASE$("abc")
        """);

    [Fact]
    public Task OnLoops() => AgreeAsync("loops", """
        FOR i = 1 TO 5
            PRINT i
        NEXT i
        FOR j = 10 TO 1 STEP -3
            PRINT j
        NEXT j
        """);

    [Fact]
    public Task OnConditions() => AgreeAsync("conditions", """
        x = 5
        IF x > 3 THEN
            PRINT "big"
        ELSE
            PRINT "small"
        END IF
        PRINT x = 5
        PRINT x = 6
        """);

    [Fact]
    public Task OnProcedures() => AgreeAsync("procedures", """
        CALL Show(3)
        PRINT Square(4)
        SUB Show(n)
            PRINT n
        END SUB
        FUNCTION Square(n)
            Square = n * n
        END FUNCTION
        """);

    [Fact]
    public Task OnArrays() => AgreeAsync("arrays", """
        DIM a(5)
        FOR i = 0 TO 5
            a(i) = i * i
        NEXT i
        FOR i = 0 TO 5
            PRINT a(i)
        NEXT i
        """);

    [Fact]
    public Task OnSelectCase() => AgreeAsync("select", """
        FOR i = 1 TO 4
            SELECT CASE i
                CASE 1
                    PRINT "one"
                CASE 2
                    PRINT "two"
                CASE ELSE
                    PRINT "many"
            END SELECT
        NEXT i
        """);

    [Fact]
    public Task OnADecimalNumber() => AgreeAsync("decimals", """
        PRINT 1 / 3
        PRINT 2 / 4
        PRINT 10 / 4
        """);

    [Fact]
    public Task OnWholeNumberTypes() => AgreeAsync("integers", """
        x% = 3.7
        PRINT x%
        y% = 3.2
        PRINT y%
        z% = -3.7
        PRINT z%
        """);

    [Fact]
    public Task OnGosub() => AgreeAsync("gosub", """
        PRINT "before"
        GOSUB Helper
        PRINT "after"
        END
        Helper:
        PRINT "inside"
        RETURN
        """);

    private static string HostRuntimeIdentifier =>
        (OperatingSystem.IsMacOS() ? "osx-" : OperatingSystem.IsWindows() ? "win-" : "linux-")
        + (NativeCompiler.HostArchitecture == "arm64" ? "arm64" : "x64");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
