using System.Diagnostics;
using Basalt.QuickBasic;

namespace Basalt.Tests;

/// <summary>
/// Compiling QuickBASIC to a native executable, and running it.
///
/// Running the binary is the point: a compiler that produces a file proves
/// nothing, and every check here reads what the program actually printed.
/// </summary>
public sealed class QuickBasicCompilerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-qb", Guid.NewGuid().ToString("N"));

    private readonly NativeCompiler _compiler = new();

    public QuickBasicCompilerTests() => Directory.CreateDirectory(_root);

    /// <summary>Compiles a program and returns what running it printed.</summary>
    private async Task<string> RunAsync(string source, string? input = null)
    {
        var output = Path.Combine(_root, "program");

        var outcome = await _compiler.CompileAsync(new CompilationRequest(source, output));

        Assert.True(outcome.Succeeded,
            $"Compilation failed.\n{string.Join("\n", outcome.Diagnostics)}\n{outcome.CompilerOutput}");

        var process = Process.Start(new ProcessStartInfo(output)
        {
            RedirectStandardOutput = true,
            RedirectStandardInput = input is not null,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var printed = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return printed;
    }

    [Fact]
    public async Task FindsTheCCompiler()
    {
        // Everything below fails for this one reason if it is missing; this
        // says so directly rather than through eight confusing failures.
        Assert.True(_compiler.IsAvailable, "clang was not found.");
    }

    [Fact]
    public async Task RunsAProgramThatPrintsText()
    {
        Assert.Contains("Hello, world", await RunAsync("PRINT \"Hello, world\""));
    }

    [Fact]
    public async Task RunsAProgramThatCounts()
    {
        const string source = """
            DIM total AS INTEGER
            total = 0
            FOR i = 1 TO 5
                total = total + i
            NEXT i
            PRINT total
            """;

        // 1 + 2 + 3 + 4 + 5.
        Assert.Contains("15", await RunAsync(source));
    }

    [Fact]
    public async Task CountsDownwardsWithANegativeStep()
    {
        const string source = """
            FOR i = 3 TO 1 STEP -1
                PRINT i
            NEXT i
            """;

        var printed = await RunAsync(source);

        Assert.Contains("3", printed);
        Assert.Contains("1", printed);
    }

    [Fact]
    public async Task ChoosesBetweenBranches()
    {
        const string source = """
            DIM n AS INTEGER
            n = 7
            IF n > 5 THEN
                PRINT "big"
            ELSE
                PRINT "small"
            END IF
            """;

        Assert.Contains("big", await RunAsync(source));
    }

    [Fact]
    public async Task RunsAWhileLoop()
    {
        const string source = """
            DIM n AS INTEGER
            n = 0
            WHILE n < 3
                n = n + 1
            WEND
            PRINT n
            """;

        Assert.Contains("3", await RunAsync(source));
    }

    [Fact]
    public async Task JoinsStrings()
    {
        const string source = """
            DIM a AS STRING
            a = "Hello, "
            PRINT a + "world"
            """;

        Assert.Contains("Hello, world", await RunAsync(source));
    }

    [Fact]
    public async Task ComparesStrings()
    {
        const string source = """
            DIM name AS STRING
            name = "ada"
            IF name = "ada" THEN
                PRINT "matched"
            END IF
            """;

        Assert.Contains("matched", await RunAsync(source));
    }

    [Fact]
    public async Task CallsAFunctionAndUsesItsResult()
    {
        const string source = """
            DECLARE FUNCTION Double%(n%)

            PRINT Double%(21)

            FUNCTION Double%(n%)
                Double% = n% * 2
            END FUNCTION
            """;

        Assert.Contains("42", await RunAsync(source));
    }

    [Fact]
    public async Task CallsASubroutine()
    {
        const string source = """
            CALL Greet

            SUB Greet
                PRINT "greeted"
            END SUB
            """;

        Assert.Contains("greeted", await RunAsync(source));
    }

    [Fact]
    public async Task PassesArgumentsToAProcedure()
    {
        const string source = """
            CALL Show(6, 7)

            SUB Show(a%, b%)
                PRINT a% * b%
            END SUB
            """;

        Assert.Contains("42", await RunAsync(source));
    }

    [Fact]
    public async Task StoresAndReadsAnArray()
    {
        const string source = """
            DIM numbers(5) AS INTEGER
            numbers(2) = 99
            PRINT numbers(2)
            """;

        Assert.Contains("99", await RunAsync(source));
    }

    [Fact]
    public async Task ReadsWhatTheUserTypes()
    {
        const string source = """
            DIM n AS INTEGER
            INPUT "Number: ", n
            PRINT n * 2
            """;

        Assert.Contains("42", await RunAsync(source, input: "21\n"));
    }

    [Fact]
    public async Task RunsASelectCase()
    {
        const string source = """
            DIM n AS INTEGER
            n = 2
            SELECT CASE n
                CASE 1
                    PRINT "one"
                CASE 2
                    PRINT "two"
                CASE ELSE
                    PRINT "other"
            END SELECT
            """;

        Assert.Contains("two", await RunAsync(source));
    }

    [Fact]
    public async Task RunsADoLoop()
    {
        const string source = """
            DIM n AS INTEGER
            n = 0
            DO WHILE n < 4
                n = n + 1
            LOOP
            PRINT n
            """;

        Assert.Contains("4", await RunAsync(source));
    }

    [Fact]
    public async Task JumpsToALabel()
    {
        const string source = """
            GOTO Finish
            PRINT "skipped"
            Finish:
            PRINT "arrived"
            """;

        var printed = await RunAsync(source);

        Assert.Contains("arrived", printed);
        Assert.DoesNotContain("skipped", printed);
    }

    [Fact]
    public async Task DividesAsIntegersWithTheBackslash()
    {
        // "\" is integer division in QuickBASIC, and "/" is not.
        Assert.Contains("3", await RunAsync("PRINT 7 \\ 2"));
    }

    [Fact]
    public async Task TakesTheRemainderWithMod()
    {
        Assert.Contains("1", await RunAsync("PRINT 7 MOD 2"));
    }

    [Fact]
    public async Task BuildsForTheOtherArchitecture()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Both architectures are native only on macOS.");

        // The point of going through C: the same program builds for another
        // machine with one flag.
        var other = NativeCompiler.HostArchitecture == "arm64" ? "x86_64" : "arm64";
        var output = Path.Combine(_root, "other-arch");

        var outcome = await _compiler.CompileAsync(
            new CompilationRequest("PRINT \"portable\"", output) { Architecture = other });

        Assert.True(outcome.Succeeded, outcome.CompilerOutput);
        Assert.True(File.Exists(output));

        // The file itself says which machine it is for.
        var described = Process.Start(new ProcessStartInfo("file", output)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var description = await described.StandardOutput.ReadToEndAsync();
        await described.WaitForExitAsync();

        Assert.Contains(other, description);
    }

    [Fact]
    public async Task RefusesToBuildAProgramWithAnError()
    {
        // Generated C from a wrong program would make the user read clang's
        // complaint about code they never wrote.
        var outcome = await _compiler.CompileAsync(
            new CompilationRequest("CALL Missing", Path.Combine(_root, "bad")));

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Diagnostics, d => d.Message.Contains("not defined"));
    }

    [Fact]
    public async Task KeepsTheGeneratedCodeWhenAskedTo()
    {
        var output = Path.Combine(_root, "kept");

        var outcome = await _compiler.CompileAsync(
            new CompilationRequest("PRINT 1", output) { KeepIntermediate = true });

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(Path.ChangeExtension(output, ".c")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
