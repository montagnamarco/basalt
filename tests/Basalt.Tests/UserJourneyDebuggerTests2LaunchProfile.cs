using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Localization;

namespace Basalt.Tests;

public sealed partial class UserJourneyDebuggerTests2
{
    private const string Profile = """
        {
          "profiles": {
            "App": {
              "commandName": "Project",
              "commandLineArgs": "alpha \"beta gamma\" 3",
              "workingDirectory": "work",
              "environmentVariables": { "BASALT_PROBE": "xyz" }
            }
          }
        }
        """;

    private async Task AssertProgramGotTheProfileAsync(TestWindow host, bool directoryToo = true)
    {
        var w = host.Window;

        Assert.True(await PumpUntilAsync(host,
            () => ConsoleTextOf(w).Contains("basalt-cwd=[", StringComparison.Ordinal),
            TimeSpan.FromSeconds(120)),
            "The program did not print its arguments: " + StateOf(w, ProgramPath));

        var text = ConsoleTextOf(w);

        Assert.Contains("basalt-args=[alpha|beta gamma|3]", text, StringComparison.Ordinal);
        Assert.Contains("basalt-env=[xyz]", text, StringComparison.Ordinal);

        // Measured: "dotnet run" (SDK 10.0.401) starts the program in the project's folder whatever the profile's
        // workingDirectory says, so Ctrl+F5 does not honour it; only F5 does.
        if (directoryToo) Assert.Contains("basalt-cwd=[work]", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task F5StartsTheProgramWithTheLaunchProfilesArgumentsEnvironmentAndDirectory()
    {
        Assert.SkipWhen(AdapterMissing || OperatingSystem.IsWindows(), "netcoredbg is not installed, or no pseudo-terminal.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.EchoArguments, Profile);
        Directory.CreateDirectory(Path.Combine(_root, "work"));

        try
        {
            PressKey(host.Window, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await AssertProgramGotTheProfileAsync(host);
        }
        finally
        {
            await host.Window.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task RunWithoutDebuggingStartsItWithTheProfilesArgumentsAndEnvironment()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "No pseudo-terminal.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.EchoArguments, Profile);
        Directory.CreateDirectory(Path.Combine(_root, "work"));

        InvokeMenu(host.Window, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        await AssertProgramGotTheProfileAsync(host, directoryToo: false);
    }
}
