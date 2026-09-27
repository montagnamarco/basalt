using Basalt.Core.Services;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Runtime;
using Basalt.Workspace.Debugging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>
/// A breakpoint set in a .vbhtml, hit by a real debugger in the running view.
/// </summary>
/// <remarks>
/// ExternalSourcePdbTests reads where the PDB says each line binds; this
/// drives netcoredbg to it, as a user pressing F9 in a template and running
/// would: the stop must be reported in the template, on the line set, with
/// the block's variables readable. Skipped where netcoredbg is not installed.
/// </remarks>
public sealed class TemplateBreakpointTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-template-debug", Guid.NewGuid().ToString("N"));

    private string _templatePath = "";
    private string _assemblyPath = "";

    private static string? AdapterPath => DebugAdapterLocator.Find();

    private const string Template = """
        @Code
            Dim greeting = "Hello"
            Dim count = greeting.Length
        End Code
        <p>@greeting, @count</p>
        @Functions
            Public Function Twice(n As Integer) As Integer
                Dim doubled = n * 2
                Return doubled
            End Function
        End Functions
        <p>@Twice(count)</p>
        """;

    private const string Program = """
        Module Program
            Sub Main()
                Console.WriteLine(New Site.Views.Home.Index().Render())
            End Sub
        End Module
        """;

    public async ValueTask InitializeAsync()
    {
        if (AdapterPath is null) return;

        Directory.CreateDirectory(_root);

        // On disk and in the PDB under the same name: the debugger matches a
        // breakpoint to a document by path.
        var template = Template.ReplaceLineEndings("\n");
        _templatePath = Path.Combine(_root, "Index.vbhtml");
        await File.WriteAllTextAsync(_templatePath, template);

        var view = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Index", "Site.Views.Home", _templatePath).Code;

        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(VbHtmlView).Assembly.Location));

        var compilation = VisualBasicCompilation.Create(
            "TemplateDebugTarget",
            [VisualBasicSyntaxTree.ParseText(view), VisualBasicSyntaxTree.ParseText(Program)],
            references,
            new VisualBasicCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Debug)
                .WithGlobalImports(GlobalImport.Parse("Microsoft.VisualBasic", "System")));

        _assemblyPath = Path.Combine(_root, "TemplateDebugTarget.dll");

        await using (var pe = File.Create(_assemblyPath))
        await using (var pdb = File.Create(Path.ChangeExtension(_assemblyPath, ".pdb")))
        {
            var emitted = compilation.Emit(pe, pdb,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        }

        // What "dotnet run" would leave beside the assembly: the runtime to
        // start it on, and the library the view derives from.
        await File.WriteAllTextAsync(Path.ChangeExtension(_assemblyPath, ".runtimeconfig.json"), $$"""
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": { "name": "Microsoft.NETCore.App", "version": "{{Environment.Version.ToString(3)}}" }
              }
            }
            """);

        File.Copy(typeof(VbHtmlView).Assembly.Location,
            Path.Combine(_root, Path.GetFileName(typeof(VbHtmlView).Assembly.Location)));
    }

    public ValueTask DisposeAsync()
    {
        ScratchFolder.Delete(_root);
        return ValueTask.CompletedTask;
    }

    private static int LineOf(string text) =>
        Template.ReplaceLineEndings("\n")[..Template.ReplaceLineEndings("\n").IndexOf(text, StringComparison.Ordinal)]
            .Count(character => character == '\n') + 1;

    /// <summary>Runs the view to a breakpoint on a template line.</summary>
    private async Task<(NetCoreDebugService Service, StackFrame Frame)> StopAtAsync(int line)
    {
        var service = new NetCoreDebugService(AdapterPath!);

        var paused = new TaskCompletionSource<StackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(_templatePath, [new Breakpoint(_templatePath, line)]);
        await service.LaunchAsync(_assemblyPath, _root);

        var frame = await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        return (service, frame);
    }

    [Fact]
    public async Task StopsInTheCodeBlockWithItsVariablesReadable()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        var line = LineOf("Dim count");
        var (service, _) = await StopAtAsync(line);

        using (service)
        {
            var stack = await service.GetCallStackAsync();

            Assert.Equal(line, stack[0].Line);
            Assert.Equal(_templatePath, stack[0].FilePath, ignoreCase: OperatingSystem.IsWindows());

            var locals = await service.GetLocalsAsync(0);

            Assert.Contains(locals, variable => variable.Name == "greeting" && variable.Value.Contains("Hello"));
        }
    }

    [Fact]
    public async Task StopsInAMethodDeclaredInFunctions()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        var line = LineOf("Return doubled");
        var (service, _) = await StopAtAsync(line);

        using (service)
        {
            var stack = await service.GetCallStackAsync();

            Assert.Equal(line, stack[0].Line);
            Assert.Contains("Twice", stack[0].Method);
            Assert.Equal("10", await service.EvaluateAsync("doubled", 0));
        }
    }
}
