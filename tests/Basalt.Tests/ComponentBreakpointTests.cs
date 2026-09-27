using Basalt.Core.Services;
using Basalt.Workspace.Debugging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>
/// A breakpoint set in a .vbrazor, hit by a real debugger while the
/// component renders and while its handler runs.
/// </summary>
/// <remarks>
/// Acceptance scenario S6, on the server side. The component is built by the
/// real generator and rendered by ASP.NET Core's HtmlRenderer, which runs
/// its markup expressions; the handler an @onclick names is then called as
/// the click would call it. Skipped where netcoredbg is not installed.
/// </remarks>
public sealed class ComponentBreakpointTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-component-debug", Guid.NewGuid().ToString("N"));

    private string _componentPath = "";
    private string _assemblyPath = "";

    private static string? AdapterPath => DebugAdapterLocator.Find();

    private const string Component = """
        <p role="status">Current count: @currentCount</p>

        <button @onclick="AddressOf IncrementCount">Click me</button>

        @Code
            Private currentCount As Integer = 41

            Public Sub IncrementCount()
                Dim before = currentCount
                currentCount = before + 1
            End Sub
        End Code
        """;

    private const string Program = """
        Imports Microsoft.AspNetCore.Components.Web
        Imports Microsoft.Extensions.DependencyInjection
        Imports Microsoft.Extensions.Logging.Abstractions

        Module DebugEntry
            Sub Main()
                Dim services = New ServiceCollection().AddLogging().BuildServiceProvider()
                Dim renderer = New HtmlRenderer(services, NullLoggerFactory.Instance)

                Dim html = renderer.Dispatcher.InvokeAsync(
                    Async Function()
                        Dim output = Await renderer.RenderComponentAsync(Of Global.Site.Components.Counter)()
                        Return output.ToHtmlString()
                    End Function).GetAwaiter().GetResult()

                Console.WriteLine(html)

                Dim counter = New Global.Site.Components.Counter()
                counter.IncrementCount()
            End Sub
        End Module
        """;

    public async ValueTask InitializeAsync()
    {
        if (AdapterPath is null) return;

        var component = Component.ReplaceLineEndings("\n");
        _componentPath = Path.Combine(_root, "Components", "Counter.vbrazor");
        Directory.CreateDirectory(Path.GetDirectoryName(_componentPath)!);
        await File.WriteAllTextAsync(_componentPath, component);

        var outcome = GeneratorRun.Run("VbComponentGenerator",
            new GeneratorRun.Setup
            {
                ProjectDirectory = _root,
                RootNamespace = "Site",
                AssemblyName = "ComponentDebugTarget",
                OptionStrict = true,
                Code = [Program]
            },
            (_componentPath, component));

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);

        var compilation = outcome.Compilation!.WithOptions(
            ((VisualBasicCompilationOptions)outcome.Compilation.Options)
                .WithOutputKind(OutputKind.ConsoleApplication)
                .WithOptimizationLevel(OptimizationLevel.Debug));

        _assemblyPath = Path.Combine(_root, "ComponentDebugTarget.dll");

        await using (var pe = File.Create(_assemblyPath))
        await using (var pdb = File.Create(Path.ChangeExtension(_assemblyPath, ".pdb")))
        {
            var emitted = compilation.Emit(pe, pdb,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        }

        // The ASP.NET Core shared framework carries Blazor's renderer; the
        // Basalt libraries the compilation referenced go beside the program.
        await File.WriteAllTextAsync(Path.ChangeExtension(_assemblyPath, ".runtimeconfig.json"), $$"""
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": { "name": "Microsoft.AspNetCore.App", "version": "{{Environment.Version.ToString(3)}}" }
              }
            }
            """);

        foreach (var library in new[] { "Basalt.Razor.Vb.dll", "Basalt.Razor.Vb.AspNetCore.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, library), Path.Combine(_root, library));
    }

    public ValueTask DisposeAsync()
    {
        ScratchFolder.Delete(_root);
        return ValueTask.CompletedTask;
    }

    private static int LineOf(string text)
    {
        var component = Component.ReplaceLineEndings("\n");

        return component[..component.IndexOf(text, StringComparison.Ordinal)].Count(character => character == '\n') + 1;
    }

    private async Task<NetCoreDebugService> StopAtAsync(int line)
    {
        var service = new NetCoreDebugService(AdapterPath!);

        var paused = new TaskCompletionSource<StackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(_componentPath, [new Breakpoint(_componentPath, line)]);
        await service.LaunchAsync(_assemblyPath, _root);

        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        return service;
    }

    [Fact]
    public async Task StopsInTheHandlerWithItsStateReadable()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        var line = LineOf("currentCount = before + 1");

        using var service = await StopAtAsync(line);
        var stack = await service.GetCallStackAsync();

        Assert.Equal(line, stack[0].Line);
        Assert.Equal(_componentPath, stack[0].FilePath, ignoreCase: OperatingSystem.IsWindows());
        Assert.Contains("IncrementCount", stack[0].Method);
        Assert.Equal("41", await service.EvaluateAsync("before", 0));
    }

    [Fact]
    public async Task StopsOnAMarkupExpressionWhileRendering()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        var line = LineOf("Current count: @currentCount");

        using var service = await StopAtAsync(line);
        var stack = await service.GetCallStackAsync();

        Assert.Equal(line, stack[0].Line);
        Assert.Equal(_componentPath, stack[0].FilePath, ignoreCase: OperatingSystem.IsWindows());
        Assert.Contains("BuildRenderTree", stack[0].Method);
    }
}
