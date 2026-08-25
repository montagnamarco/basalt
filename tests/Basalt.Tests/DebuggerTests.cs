using System.Diagnostics;
using Basalt.Core.Services;
using Basalt.Workspace.Debugging;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>
/// Debugging a real Visual Basic program with a real debugger.
///
/// These tests drive netcoredbg rather than a stand-in. A mock would only
/// prove that the client talks to the mock; the protocol's ordering rules —
/// which cost real time to discover — are exactly what needs covering.
/// Everything is skipped when the adapter is not installed, so a machine
/// without it still gets a green run.
/// </summary>
public sealed class DebuggerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-debug", Guid.NewGuid().ToString("N"));

    private string _sourcePath = "";
    private string _assemblyPath = "";

    private static string? AdapterPath => DebugAdapterLocator.Find();

    private const string Program = """
        Namespace DebugTarget
            Module Program
                Sub Main()
                    Dim total As Integer = 0
                    For i As Integer = 1 To 5
                        total += i
                    Next
                    Console.WriteLine(total)
                End Sub
            End Module
        End Namespace
        """;

    public async ValueTask InitializeAsync()
    {
        if (AdapterPath is null) return;

        Directory.CreateDirectory(_root);

        _sourcePath = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(_sourcePath, Program);

        await File.WriteAllTextAsync(Path.Combine(_root, "DebugTarget.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <DebugType>portable</DebugType>
              </PropertyGroup>
            </Project>
            """);

        // Debug symbols are what the adapter maps lines through, so the target
        // has to be a genuinely built assembly.
        var build = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "build", "-c", "Debug", "--nologo", "-v", "q" },
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        await build.WaitForExitAsync();

        _assemblyPath = Path.Combine(
            _root, "bin", "Debug", "net10.0", "DebugTarget.dll");
    }

    /// <summary>Runs to the breakpoint on the line adding to the total.</summary>
    private async Task<NetCoreDebugService> StopAtLoopBodyAsync()
    {
        var service = new NetCoreDebugService(AdapterPath!);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(_sourcePath, [new Breakpoint(_sourcePath, 6)]);
        await service.LaunchAsync(_assemblyPath, _root);

        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        return service;
    }

    [Fact]
    public async Task StopsWhereTheBreakpointWasSet()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = await StopAtLoopBodyAsync();

        var stack = await service.GetCallStackAsync();

        Assert.NotEmpty(stack);
        Assert.Equal(6, stack[0].Line);
        Assert.Contains("Main", stack[0].Method);
    }

    [Fact]
    public async Task ReadsTheLocalsOfTheStoppedFrame()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = await StopAtLoopBodyAsync();

        var locals = await service.GetLocalsAsync(0);

        // On the first pass the loop has counted once but added nothing yet.
        Assert.Equal("0", locals.Single(v => v.Name == "total").Value);
        Assert.Equal("1", locals.Single(v => v.Name == "i").Value);
    }

    [Fact]
    public async Task EvaluatesAnExpressionInTheStoppedFrame()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = await StopAtLoopBodyAsync();

        Assert.Equal("1", await service.EvaluateAsync("i", 0));
    }

    [Fact]
    public async Task StepsToTheNextLine()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = await StopAtLoopBodyAsync();

        var stepped = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => stepped.TrySetResult(frame);

        await service.StepOverAsync();

        var frame = await stepped.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Line 6 is the body; stepping over it reaches the Next on line 7.
        Assert.Equal(7, frame.Line);
    }

    [Fact]
    public async Task ComesBackAroundTheLoopWhenContinued()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = await StopAtLoopBodyAsync();

        var again = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => again.TrySetResult(frame);

        await service.ContinueAsync();
        await again.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var locals = await service.GetLocalsAsync(0);

        // Second time round: one iteration has been added and the counter moved on.
        Assert.Equal("1", locals.Single(v => v.Name == "total").Value);
        Assert.Equal("2", locals.Single(v => v.Name == "i").Value);
    }

    [Fact]
    public async Task StopsOnlyWhenAConditionHolds()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = new NetCoreDebugService(AdapterPath!);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(_sourcePath,
            [new Breakpoint(_sourcePath, 6, Condition: "i = 4")]);

        await service.LaunchAsync(_assemblyPath, _root);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var locals = await service.GetLocalsAsync(0);

        Assert.Equal("4", locals.Single(v => v.Name == "i").Value);
        // Three iterations have already run: 1 + 2 + 3.
        Assert.Equal("6", locals.Single(v => v.Name == "total").Value);
    }

    [Fact]
    public async Task ReportsTheProgramsOutputAndItsExit()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = new NetCoreDebugService(AdapterPath!);

        var output = new List<string>();
        service.OutputReceived += (_, text) => output.Add(text);

        var exited = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Exited += (_, code) => exited.TrySetResult(code);

        // No breakpoints: the program runs to completion.
        await service.LaunchAsync(_assemblyPath, _root);

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // 1 + 2 + 3 + 4 + 5.
        Assert.Contains(output, line => line.Contains("15", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsBreakpointsSetBeforeTheSessionStarted()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        // Breakpoints are usually placed while editing, long before anything runs.
        using var service = new NetCoreDebugService(AdapterPath!);

        await service.SetBreakpointsAsync(_sourcePath, [new Breakpoint(_sourcePath, 8)]);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.LaunchAsync(_assemblyPath, _root);

        var frame = await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(8, frame.Line);
    }

    [Fact]
    public async Task IgnoresABreakpointThatIsSwitchedOff()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var service = new NetCoreDebugService(AdapterPath!);

        var exited = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Exited += (_, code) => exited.TrySetResult(code);

        // Recorded rather than asserted here: this handler runs on a background
        // thread, where a failed assertion has no test to fail.
        var stopped = false;
        service.Paused += (_, _) => stopped = true;

        await service.SetBreakpointsAsync(_sourcePath,
            [new Breakpoint(_sourcePath, 6, Enabled: false)]);

        await service.LaunchAsync(_assemblyPath, _root);

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(stopped, "A disabled breakpoint stopped the program.");
    }

    [Fact]
    public async Task StopsEvenWhenTheSourcePathGoesThroughASymlink()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symlinked temp paths are a Unix concern.");

        // The debugger matches source paths as text, so a path reached through
        // a symlink matches nothing and every breakpoint is quietly ignored.
        // On macOS /tmp is such a link, which makes this the common case rather
        // than an exotic one.
        var viaLink = Path.Combine("/tmp", Path.GetRelativePath("/private/tmp", _sourcePath));

        Assert.SkipWhen(!File.Exists(viaLink), "The temp directory is not reached through a link.");
        Assert.NotEqual(viaLink, _sourcePath);

        using var service = new NetCoreDebugService(AdapterPath!);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(viaLink, [new Breakpoint(viaLink, 6)]);
        await service.LaunchAsync(_assemblyPath, _root);

        var frame = await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(6, frame.Line);

        // Reported back in the spelling it was given, so the IDE can match it
        // against the tab the user already has open.
        Assert.Equal(viaLink, frame.FilePath);
    }

    public ValueTask DisposeAsync()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }
}
