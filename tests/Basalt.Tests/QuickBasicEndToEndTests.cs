using System.Diagnostics;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Extensibility;
using Basalt.QuickBasic;

namespace Basalt.Tests;

/// <summary>
/// A QuickBASIC program written in the IDE, run as a native executable.
///
/// The whole way through: the template the user picks, the backend the IDE
/// reaches through the language contracts, and the binary that comes out.
/// </summary>
public sealed class QuickBasicEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-qb-e2e", Guid.NewGuid().ToString("N"));

    public QuickBasicEndToEndTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task WritesAProgramFromTheTemplate()
    {
        var result = await SolutionTemplates.CreateAsync(
            _root, "Squares", ProjectTemplate.QuickBasic);

        Assert.True(File.Exists(result.ProjectPath));
        Assert.EndsWith(".bas", result.ProjectPath);
    }

    [Fact]
    public async Task TheTemplateProgramHasNoErrors()
    {
        // A new project that does not compile is worse than no template.
        var result = await SolutionTemplates.CreateAsync(
            _root, "Squares", ProjectTemplate.QuickBasic);

        var source = await File.ReadAllTextAsync(result.ProjectPath);

        var program = Parser.Parse(source);
        var symbols = SymbolTable.Build(program);

        Assert.Empty(program.Diagnostics);
        Assert.Empty(symbols.Diagnostics);
    }

    [Fact]
    public async Task BuildsAndRunsTheTemplateProgram()
    {
        var result = await SolutionTemplates.CreateAsync(
            _root, "Squares", ProjectTemplate.QuickBasic);

        // Through the contract the IDE uses, not the compiler directly.
        var backend = new QuickBasicLanguageProvider().Compiler!;

        var output = Path.Combine(_root, "Squares", "Squares.out");

        var compiled = await backend.CompileAsync(
            result.ProjectPath,
            new CompilationTarget(HostRuntimeIdentifier) { OutputPath = output });

        Assert.True(compiled.Succeeded,
            string.Join("\n", compiled.Diagnostics.Select(d => d.Message)));

        var process = Process.Start(new ProcessStartInfo(output)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var printed = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        // 1 + 4 + 9 + 16 + 25.
        Assert.Contains("55", printed);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task ReportsBuildFailuresThroughTheContract()
    {
        var source = Path.Combine(_root, "broken.bas");
        await File.WriteAllTextAsync(source, "CALL NotThere");

        var backend = new QuickBasicLanguageProvider().Compiler!;

        var compiled = await backend.CompileAsync(
            source,
            new CompilationTarget(HostRuntimeIdentifier)
            {
                OutputPath = Path.Combine(_root, "broken.out")
            });

        Assert.False(compiled.Succeeded);
        Assert.All(compiled.Diagnostics, d => Assert.Equal(source, d.FilePath));
    }

    [Fact]
    public async Task TimesTheBuild()
    {
        var source = Path.Combine(_root, "quick.bas");
        await File.WriteAllTextAsync(source, "PRINT 1");

        var compiled = await new QuickBasicLanguageProvider().Compiler!.CompileAsync(
            source,
            new CompilationTarget(HostRuntimeIdentifier)
            {
                OutputPath = Path.Combine(_root, "quick.out")
            });

        Assert.True(compiled.Duration > TimeSpan.Zero);
    }

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
