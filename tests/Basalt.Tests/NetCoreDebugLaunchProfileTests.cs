using System.Text.Json.Nodes;
using Basalt.Core.Services;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// F5 starts a program with the arguments, environment and working directory of its launch profile, as "dotnet run"
/// (Ctrl+F5) does: the profile is read from My Project/launchSettings.json, and what it gives is sent in the launch.
/// </summary>
public sealed class NetCoreDebugLaunchProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-launch-profile-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string WriteProject(string launchSettings)
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, NetCoreDebugTargets.ExeProject);
        Directory.CreateDirectory(Path.Combine(_root, "My Project"));
        File.WriteAllText(Path.Combine(_root, "My Project", "launchSettings.json"), launchSettings);

        return project;
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("--name \"two words\" --flag", new[] { "--name", "two words", "--flag" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("C:\\dir\\file.txt", new[] { "C:\\dir\\file.txt" })]
    [InlineData("\"C:\\dir with space\\\\\" next", new[] { "C:\\dir with space\\", "next" })]
    public void ACommandLineIsSplitAsWindowsAndDotnetRunSplitIt(string commandLine, string[] expected) =>
        Assert.Equal(expected, DebugLaunchProfile.SplitCommandLine(commandLine));

    [Fact]
    public void TheProfileGivesArgumentsEnvironmentAndWorkingDirectory()
    {
        var project = WriteProject("""
            {
              "profiles": {
                "App": {
                  "commandName": "Project",
                  "commandLineArgs": "alpha \"beta gamma\" 3",
                  "workingDirectory": "work",
                  "environmentVariables": { "BASALT_PROBE": "xyz", "OTHER": "1" }
                }
              }
            }
            """);

        var options = DebugLaunchProfile.For(project, defaultDirectory: _root);

        Assert.Equal(["alpha", "beta gamma", "3"], options.Arguments);
        Assert.Equal("xyz", options.Environment["BASALT_PROBE"]);
        Assert.Equal("1", options.Environment["OTHER"]);

        // Relative to the project's folder, as the profile is.
        Assert.Equal(Path.Combine(_root, "work"), options.WorkingDirectory);
    }

    [Fact]
    public void AProfileWithoutAWorkingDirectoryStartsInTheDefaultOne()
    {
        var project = WriteProject("""{ "profiles": { "App": { "commandName": "Project" } } }""");

        var options = DebugLaunchProfile.For(project, defaultDirectory: "/somewhere");

        Assert.Equal("/somewhere", options.WorkingDirectory);
        Assert.Empty(options.Arguments);
        Assert.Empty(options.Environment);
    }

    [Fact]
    public void AProjectWithoutALaunchProfileStartsAsItDid()
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, NetCoreDebugTargets.ExeProject);

        var options = DebugLaunchProfile.For(project, defaultDirectory: _root);

        Assert.Equal(_root, options.WorkingDirectory);
        Assert.Empty(options.Arguments);
        Assert.Empty(options.Environment);
    }

    [Fact]
    public void AnApplicationUrlBecomesTheHostsUrlsAsDotnetRunDoesIt()
    {
        var project = WriteProject("""
            { "profiles": { "App": { "commandName": "Project", "applicationUrl": "http://localhost:5123",
                "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" } } } }
            """);

        var options = DebugLaunchProfile.For(project, _root);

        Assert.Equal("http://localhost:5123", options.Environment["ASPNETCORE_URLS"]);
        Assert.Equal("Development", options.Environment["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public async Task TheLaunchRequestCarriesTheArgumentsTheEnvironmentAndTheDirectory()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.LaunchAsync("/app/App.dll", new LaunchOptions(
            "/app/work", ["alpha", "beta gamma"], new Dictionary<string, string> { ["BASALT_PROBE"] = "xyz" }));

        var launch = dap.ArgumentsOf("launch")!;

        Assert.Equal(["alpha", "beta gamma"], launch["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal("xyz", launch["env"]!["BASALT_PROBE"]!.GetValue<string>());
        Assert.Equal("/app/work", launch["cwd"]!.GetValue<string>());
    }

    [Fact]
    public async Task ALaunchWithoutAProfileSendsNoArgumentsOrEnvironment()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.LaunchAsync("/app/App.dll", "/app");

        var launch = dap.ArgumentsOf("launch")!;

        Assert.False(launch.ContainsKey("args"));
        Assert.False(launch.ContainsKey("env"));
    }

    [Fact]
    public async Task TheRealDebuggerStartsTheProgramWithTheProfilesArgumentsEnvironmentAndDirectory()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is not { } adapter, "netcoredbg is not installed.");

        var (project, _, assembly) = await NetCoreDebugTargets.BuildAsync(_root, "App", NetCoreDebugTargets.EchoArguments);
        Directory.CreateDirectory(Path.Combine(_root, "work"));
        File.WriteAllText(Path.Combine(_root, "launchSettings.json"), "{}");

        var options = new LaunchOptions(
            Path.Combine(_root, "work"),
            DebugLaunchProfile.SplitCommandLine("alpha \"beta gamma\" 3"),
            new Dictionary<string, string> { ["BASALT_PROBE"] = "xyz" });

        var output = new System.Text.StringBuilder();
        var exited = new TaskCompletionSource();
        using var service = new NetCoreDebugService(DebugAdapterLocator.Find()!);
        service.OutputReceived += (_, text) => { lock (output) output.Append(text); };
        service.Exited += (_, _) => exited.TrySetResult();

        await service.LaunchAsync(assembly, options);
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(60));

        string text;
        lock (output) text = output.ToString();

        Assert.Contains("basalt-args=[alpha|beta gamma|3]", text, StringComparison.Ordinal);
        Assert.Contains("basalt-env=[xyz]", text, StringComparison.Ordinal);
        Assert.Contains("basalt-cwd=[work]", text, StringComparison.Ordinal);
    }
}
