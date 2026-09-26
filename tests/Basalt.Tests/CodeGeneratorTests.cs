using Basalt.QuickBasic;
using Basalt.QuickBasic.CodeGeneration;

namespace Basalt.Tests;

/// <summary>The choice of what a QuickBASIC program is turned into.</summary>
public sealed class CodeGeneratorTests
{
    private static (Basalt.QuickBasic.Program Program, SymbolTable Symbols) Read(string source)
    {
        var program = Parser.Parse(source);

        return (program, SymbolTable.Build(program));
    }

    [Fact]
    public void OffersTheTargetsThePlanNames()
    {
        var ids = CodeGenerators.All.Select(g => g.Id).ToList();

        Assert.Contains("c", ids);
        Assert.Contains("vbnet", ids);
        Assert.Contains("native", ids);
    }

    [Fact]
    public void KeepsCAsTheOneUsedWhenNoneIsAskedFor()
    {
        // It is the target that has always worked, so changing the default
        // would change what an existing project builds.
        Assert.Equal("c", CodeGenerators.Default.Id);
    }

    [Fact]
    public void FindsATargetByItsName()
    {
        Assert.Equal("vbnet", CodeGenerators.ById("vbnet")?.Id);
        Assert.Equal("vbnet", CodeGenerators.ById("VBNET")?.Id);
    }

    [Fact]
    public void SaysNoToATargetThatDoesNotExist()
    {
        // Rather than falling back to C: a build asked for something, and
        // quietly building something else is the worse answer.
        Assert.Null(CodeGenerators.ById("llvm"));
    }

    [Fact]
    public void LeavesTheUnfinishedTargetOutOfTheBuildableOnes()
    {
        var buildable = CodeGenerators.Buildable.Select(g => g.Id).ToList();

        Assert.Contains("c", buildable);
        Assert.Contains("vbnet", buildable);
        Assert.DoesNotContain("native", buildable);
    }

    [Fact]
    public void SaysPlainlyThatTheNativeTargetIsNotFinished()
    {
        var native = CodeGenerators.ById("native")!;

        Assert.False(native.IsComplete);
        Assert.Contains("skeleton", native.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCTargetStillEmitsWhatItAlwaysDid()
    {
        // The target wrapper must not have changed the backend it wraps.
        var (program, symbols) = Read("PRINT 1 + 2");

        Assert.Equal(
            CodeWriter.Generate(program, symbols),
            new CCodeGenerator().Generate(program, symbols));
    }

    [Fact]
    public void LowersArithmeticToTheIntermediateRepresentation()
    {
        var (program, _) = Read("x = 1 + 2 * 3");

        var lowered = new NativeCodeGenerator().Lower(program);

        // Three-address and flat: one operation each, nothing nested.
        Assert.Contains(lowered, i => i.Op == IrOp.Add);
        Assert.Contains(lowered, i => i.Op == IrOp.Multiply);
        Assert.All(lowered, i => Assert.True(
            i.Op != IrOp.Add || (i.Left is not null && i.Right is not null)));
    }

    [Fact]
    public void EndsTheLoweredProgramWithAReturn()
    {
        var (program, _) = Read("x = 1");

        Assert.Equal(IrOp.Return, new NativeCodeGenerator().Lower(program)[^1].Op);
    }

    [Fact]
    public void PassesOverWhatItCannotLowerYet()
    {
        // A string has no runtime in the native backend, so it is left out
        // rather than lowered wrongly.
        var (program, _) = Read("""
            a$ = "text"
            x = 1 + 2
            """);

        var lowered = new NativeCodeGenerator().Lower(program);

        Assert.Contains(lowered, i => i.Op == IrOp.Add);
        Assert.DoesNotContain(lowered, i => i.Left == "text");
    }

    [Fact]
    public void SaysInTheOutputThatTheNativeTargetIsASkeleton()
    {
        // Someone who runs it should learn that from the file, not by
        // wondering why it does not assemble.
        var (program, symbols) = Read("PRINT 1");

        var output = new NativeCodeGenerator().Generate(program, symbols);

        Assert.Contains("skeleton", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectsAnArm64InstructionForEachLoweredOne()
    {
        var (program, _) = Read("x = 1 + 2");

        var generator = new NativeCodeGenerator();
        var lowered = generator.Lower(program);

        var selected = generator.SelectArm64(lowered);

        // One line each, plus the label at the top.
        Assert.Equal(lowered.Count + 1, selected.Count);
    }

    /// <summary>Compiling through the language provider, choosing a target.</summary>
    public sealed class ChoosingATarget : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "basalt-target", Guid.NewGuid().ToString("N"));

        public ChoosingATarget() => Directory.CreateDirectory(_root);

        private async Task<(Basalt.Extensibility.CompilationResult Result, string Output)>
            BuildAsync(string source, string? target)
        {
            var path = Path.Combine(_root, "Program.bas");
            var output = Path.Combine(_root, "Program.out");

            await File.WriteAllTextAsync(path, source);

            var result = await new QuickBasicLanguageProvider().Compiler!.CompileAsync(
                path,
                new Basalt.Extensibility.CompilationTarget(HostRuntimeIdentifier)
                {
                    OutputPath = output,
                    CodeTarget = target
                });

            return (result, output);
        }

        [Fact]
        public async Task BuildsCWhenNoTargetIsNamed()
        {
            Clang.SkipUnlessInstalled();

            // What a project that predates the choice still gets.
            var (result, _) = await BuildAsync("PRINT 1", null);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task WritesVisualBasicWhenThatTargetIsNamed()
        {
            var (result, output) = await BuildAsync("PRINT 1", "vbnet");

            Assert.True(result.Succeeded,
                string.Join("\n", result.Diagnostics.Select(d => d.Message)));

            var written = Path.ChangeExtension(output, ".vb");

            Assert.True(File.Exists(written));
            Assert.Contains("Option Strict Off", await File.ReadAllTextAsync(written),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task SaysNoToATargetThatDoesNotExist()
        {
            var (result, _) = await BuildAsync("PRINT 1", "llvm");

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, d => d.Id == "QB110");
        }

        [Fact]
        public async Task WritesTheNativeSkeletonButDoesNotClaimItBuilds()
        {
            var (result, output) = await BuildAsync("x = 1 + 2", "native");

            // The file is there to look at, and the result says plainly that
            // it is not something that can be run.
            Assert.True(File.Exists(Path.ChangeExtension(output, ".s")));
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, d => d.Id == "QB112");
        }

        [Fact]
        public async Task ReportsAProgramThatDoesNotReadWhicheverTargetIsAsked()
        {
            var (result, _) = await BuildAsync("FOR i = 1 TO", "vbnet");

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Diagnostics);
        }

        private static string HostRuntimeIdentifier =>
            (OperatingSystem.IsMacOS() ? "osx-" : OperatingSystem.IsWindows() ? "win-" : "linux-")
            + (NativeCompiler.HostArchitecture == "arm64" ? "arm64" : "x64");

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }
    }
}
