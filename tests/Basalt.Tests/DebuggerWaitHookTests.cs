using Basalt.Workspace.Debugging;
using Basalt.Workspace.Immediate;

namespace Basalt.Tests;

/// <summary>
/// The startup hook that holds a program until the debugger is attached: compiled once per runtime, and joined
/// to any hooks the user's own environment already names.
/// </summary>
public sealed class DebuggerWaitHookTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("basalt-wait-hook-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CompilesOncePerRuntimeVersion()
    {
        var runtime = SharedFrameworkLocator.Find(null);
        Assert.SkipWhen(runtime is null, "No .NET runtime was found to compile against.");

        var first = DebuggerWaitHook.EnsureFor(runtime!, _cache);
        var written = File.GetLastWriteTimeUtc(first);
        var second = DebuggerWaitHook.EnsureFor(runtime!, _cache);

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        Assert.Contains(runtime!.Version, first, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsItsHookFirstAndKeepsTheUsersOwnForTheProgramsChildren()
    {
        var environment = DebuggerWaitHook.EnvironmentFor("/hooks/wait.dll", 30, "/user/hook.dll");

        Assert.Equal("/hooks/wait.dll" + Path.PathSeparator + "/user/hook.dll", environment[DebuggerWaitHook.StartupHooksVariable]);
        Assert.Equal("30", environment[DebuggerWaitHook.WaitVariable]);
        Assert.Equal("/user/hook.dll", environment[DebuggerWaitHook.UserHooksVariable]);

        var plain = DebuggerWaitHook.EnvironmentFor("/hooks/wait.dll", 30, null);
        Assert.Equal("/hooks/wait.dll", plain[DebuggerWaitHook.StartupHooksVariable]);
        Assert.False(plain.ContainsKey(DebuggerWaitHook.UserHooksVariable));
    }

    /// <remarks>
    /// The hook run for real, with no debugger to attach: the program must be held for the time given, then go on,
    /// and must not pass the wait on to what it starts.
    /// </remarks>
    [Fact]
    public async Task HoldsAProgramUntilItsTimeRunsOutAndThenClearsItselfForChildren()
    {
        var runtime = SharedFrameworkLocator.Find(10);
        Assert.SkipWhen(runtime is null, "No .NET 10 runtime was found.");

        var hook = DebuggerWaitHook.EnsureFor(runtime!, _cache);
        var project = Path.Combine(_cache, "Held");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "Held.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(project, "Program.vb"), """
            Module Program
                Sub Main()
                    Console.WriteLine("hooks=[" & Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS") & "] wait=[" & Environment.GetEnvironmentVariable("BASALT_WAIT_FOR_DEBUGGER") & "]")
                End Sub
            End Module
            """);

        var build = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", ["build", project, "-v", "q", "-o", Path.Combine(project, "out")])
            { RedirectStandardOutput = true, RedirectStandardError = true })!;
        await build.WaitForExitAsync();
        Assert.Equal(0, build.ExitCode);

        var start = new System.Diagnostics.ProcessStartInfo("dotnet", [Path.Combine(project, "out", "Held.dll")]) { RedirectStandardOutput = true };
        foreach (var (name, value) in DebuggerWaitHook.EnvironmentFor(hook, 2, null)) start.Environment[name] = value;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = System.Diagnostics.Process.Start(start)!;
        var printed = await run.StandardOutput.ReadToEndAsync();
        await run.WaitForExitAsync();

        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1.8), $"The program was not held: it ended after {clock.Elapsed}.");
        Assert.Contains("hooks=[] wait=[]", printed, StringComparison.Ordinal);
    }
}
