using System.Diagnostics;
using Basalt.QuickBasic;
using Basalt.QuickBasic.CodeGeneration;

namespace Basalt.Tests;

/// <summary>
/// QuickBASIC turned into Visual Basic .NET.
///
/// The check is the same one the interpreter had to pass: the generated
/// program is built and run, and what it prints has to match. A target that
/// produces plausible-looking VB which prints something else is worse than no
/// target at all, because it looks like it worked.
/// </summary>
public sealed class VbNetTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-vbtarget", Guid.NewGuid().ToString("N"));

    public VbNetTargetTests() => Directory.CreateDirectory(_root);

    private static string GenerateVb(string source)
    {
        var program = Parser.Parse(source);

        Assert.Empty(program.Diagnostics);

        return new VisualBasicCodeGenerator().Generate(program, SymbolTable.Build(program));
    }

    /// <summary>Builds and runs the generated VB, giving back what it printed.</summary>
    private async Task<string> RunVbAsync(string source, string name)
    {
        var directory = Path.Combine(_root, name);

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.vb"), GenerateVb(source));

        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <StartupObject>QuickBasicProgram</StartupObject>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var run = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "run", "--project", directory, "--verbosity", "quiet" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory
        })!;

        var output = await run.StandardOutput.ReadToEndAsync();
        var errors = await run.StandardError.ReadToEndAsync();

        await run.WaitForExitAsync();

        Assert.True(run.ExitCode == 0,
            $"The generated VB did not build or run:\n{errors}\n\n{GenerateVb(source)}");

        return output;
    }

    /// <summary>What the C target prints, as the answer to compare against.</summary>
    private async Task<string> RunCAsync(string source, string name)
    {
        Clang.SkipUnlessInstalled();

        var path = Path.Combine(_root, $"{name}.bas");
        var binary = Path.Combine(_root, $"{name}-c");

        await File.WriteAllTextAsync(path, source);

        var compiled = await new QuickBasicLanguageProvider().Compiler!.CompileAsync(
            path, new Basalt.Extensibility.CompilationTarget(HostRuntimeIdentifier)
            {
                OutputPath = binary
            });

        Assert.True(compiled.Succeeded,
            string.Join("\n", compiled.Diagnostics.Select(d => d.Message)));

        using var process = Process.Start(new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        return output;
    }

    /// <summary>Runs a program both ways and insists they agree.</summary>
    private async Task AgreeAsync(string name, string source)
    {
        var fromC = await RunCAsync(source, name);
        var fromVb = await RunVbAsync(source, name);

        Assert.Equal(fromC, fromVb);
    }

    [Fact]
    public void NamesItselfForTheProjectSettings()
    {
        var generator = new VisualBasicCodeGenerator();

        Assert.Equal("vbnet", generator.Id);
        Assert.Equal(".vb", generator.FileExtension);
        Assert.True(generator.IsComplete);
    }

    [Fact]
    public void TurnsOffStrictTypingBecauseQuickBasicHasNone()
    {
        var vb = GenerateVb("x = 1");

        Assert.Contains("Option Strict Off", vb, StringComparison.Ordinal);
        Assert.Contains("Option Explicit Off", vb, StringComparison.Ordinal);
    }

    [Fact]
    public Task OnPrintingNumbers() => AgreeAsync("numbers", """
        PRINT 1
        PRINT -1
        PRINT 0
        PRINT 42
        """);

    [Fact]
    public Task OnArithmetic() => AgreeAsync("arithmetic", """
        PRINT 1 + 2 * 3
        PRINT (1 + 2) * 3
        PRINT 7 \ 2
        PRINT 7 MOD 2
        PRINT 1 / 3
        """);

    [Fact]
    public Task OnComparisons() => AgreeAsync("comparisons", """
        PRINT 1 = 1
        PRINT 1 = 2
        PRINT 3 > 2
        """);

    [Fact]
    public Task OnStrings() => AgreeAsync("strings", """
        PRINT "hello"
        PRINT LEN("hello")
        PRINT UCASE$("abc")
        PRINT LEFT$("abcdef", 3)
        """);

    [Fact]
    public Task OnLoops() => AgreeAsync("loops", """
        FOR i = 1 TO 5
            PRINT i
        NEXT i
        """);

    [Fact]
    public Task OnConditions() => AgreeAsync("conditions", """
        x = 5
        IF x > 3 THEN
            PRINT "big"
        ELSE
            PRINT "small"
        END IF
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
