using System.Diagnostics;
using System.Xml.Linq;
using Basalt.Core.Commands;
using Avalonia.Headless.XUnit;
using Basalt.Core.Settings;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Publishing;

namespace Basalt.Tests;

/// <summary>
/// "File > Make executable": the publish command line per platform, the
/// compatibility properties of a Visual Studio WinForms project, cancelling,
/// and what lands in the output folder (the macOS bundle, the Unix bit).
/// </summary>
public sealed class MakeExecutableTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-makeexe-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Project(string properties, string items = "") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            {properties}
          </PropertyGroup>
          {items}
        </Project>
        """;

    /// <summary>A process that exits at once and ignores the publish arguments.</summary>
    private static ProcessStartInfo Harmless() => OperatingSystem.IsWindows()
        ? new ProcessStartInfo("cmd", ["/c", "exit 0"]) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
        : new ProcessStartInfo("sh", ["-c", "exit 0"]) { RedirectStandardOutput = true, RedirectStandardError = true };

    private static ProcessStartInfo Sleeping() => OperatingSystem.IsWindows()
        ? new ProcessStartInfo("cmd", ["/c", "ping -n 60 127.0.0.1"]) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
        : new ProcessStartInfo("sh", ["-c", "sleep 60"]) { RedirectStandardOutput = true, RedirectStandardError = true };

    /// <summary>
    /// A service whose "publish" only writes the single file a real one would
    /// leave in the folder named by <c>-o</c>, so the post-processing is real
    /// and the SDK and the network are not needed.
    /// </summary>
    private static PublishService FakePublish(string projectName, params string[] extraFiles) =>
        new()
        {
            Compatibility = new WindowsDesktopCompatibilitySession(_ => WindowsDesktopRetargeting.None),
            CreateStartInfo = (_, arguments) =>
            {
                var folder = arguments[arguments.ToList().IndexOf("-o") + 1];
                var rid = arguments[arguments.ToList().IndexOf("-r") + 1];
                Directory.CreateDirectory(folder);
                var file = rid.StartsWith("win", StringComparison.Ordinal) ? projectName + ".exe" : projectName;
                File.WriteAllText(Path.Combine(folder, file), "binary");
                File.WriteAllText(Path.Combine(folder, projectName + ".pdb"), "symbols");
                foreach (var extra in extraFiles) File.WriteAllText(Path.Combine(folder, extra), "content");
                return Harmless();
            }
        };

    // ---- the command line --------------------------------------------------

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    [InlineData("osx-arm64")]
    [InlineData("osx-x64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    public void EveryTargetGetsASelfContainedSingleFilePublish(string rid)
    {
        var target = PublishTarget.Find(rid)!;

        var arguments = PublishService.BuildArguments("/p/App.vbproj", target, "/p/dist/" + rid, "Release", []);

        Assert.Equal(["publish", "/p/App.vbproj"], arguments.Take(2));
        Assert.Contains("--self-contained", arguments);
        Assert.Contains("-p:PublishSingleFile=true", arguments);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", arguments);
        Assert.Equal(rid, arguments[arguments.ToList().IndexOf("-r") + 1]);
        Assert.Equal("Release", arguments[arguments.ToList().IndexOf("-c") + 1]);
        Assert.Equal("/p/dist/" + rid, arguments[arguments.ToList().IndexOf("-o") + 1]);
    }

    [Fact]
    public void SixPlatformsAreOfferedAndTheCurrentMachineIsOneOfThem()
    {
        Assert.Equal(6, PublishTarget.All.Count);
        Assert.Contains(PublishTarget.Current(), PublishTarget.All);
        Assert.Equal(Path.Combine(_root, "bin", "Publish"), PublishRequest.DefaultOutputRoot(Path.Combine(_root, "App.vbproj")));
    }

    [Fact]
    public void TheCommandIsRegisteredWithoutAGestureThatIsAltGrOnEuropeanLayouts()
    {
        foreach (var scheme in Enum.GetValues<KeyboardScheme>())
        {
            var command = IdeCommands.CreateRegistry(scheme).All.Single(c => c.Id == IdeCommands.FileMakeExecutable);

            Assert.Null(command.DefaultGesture);
        }
    }

    [Fact]
    public void RelativeAndHomeOutputFoldersAreResolvedAgainstTheProject()
    {
        var project = Path.Combine(_root, "App", "App.vbproj");
        var targets = new[] { PublishTarget.Find("linux-x64")! };

        Assert.Equal(Path.Combine(_root, "App", "out", "linux-x64"),
            new PublishRequest(project, targets, "out").OutputFolderFor(targets[0]));
        Assert.Equal(Path.Combine(_root, "shared", "linux-x64"),
            new PublishRequest(project, targets, Path.Combine("..", "shared")).OutputFolderFor(targets[0]));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "apps", "linux-x64"),
            new PublishRequest(project, targets, "~/apps").OutputFolderFor(targets[0]));
    }

    [Fact]
    public async Task VisualStudioWinFormsProjectIsPublishedWithTheBuildsCompatibilityProperties()
    {
        // Same decision the build makes, against a source root that need only look like one.
        Write("FakeSources/Basalt.Forms/Basalt.Forms.csproj", "<Project />");
        var sources = Path.Combine(_root, "FakeSources");
        var session = new WindowsDesktopCompatibilitySession(
            path => WindowsDesktopProjectCompatibility.Resolve(path, isWindows: false, sources));
        var forms = Write("Forms/Forms.vbproj", Project(
            "<OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms>"));

        var service = new PublishService { Compatibility = session, CreateStartInfo = (_, _) => Harmless() };
        await service.PublishAsync(new PublishRequest(forms, [PublishTarget.Find("linux-x64")!], Path.Combine(_root, "dist")));

        var arguments = Assert.Single(service.StartedArguments);
        var expected = await session.GetArgumentsAsync(forms);
        Assert.NotEmpty(expected);
        Assert.Contains(arguments, a => a.StartsWith("-p:DirectoryBuildPropsPath=", StringComparison.Ordinal));
        Assert.Contains(arguments, a => a.StartsWith("-p:BeforeMicrosoftNETSdkTargets=", StringComparison.Ordinal));
        Assert.Equal(expected, arguments.Skip(arguments.Count - expected.Count));
    }

    [Fact]
    public async Task PlainProjectGetsNoCompatibilityProperties()
    {
        Write("FakeSources/Basalt.Forms/Basalt.Forms.csproj", "<Project />");
        var sources = Path.Combine(_root, "FakeSources");
        var session = new WindowsDesktopCompatibilitySession(
            path => WindowsDesktopProjectCompatibility.Resolve(path, isWindows: false, sources));
        var plain = Write("Plain/Plain.vbproj", Project("<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>"));

        var service = new PublishService { Compatibility = session, CreateStartInfo = (_, _) => Harmless() };
        await service.PublishAsync(new PublishRequest(plain, [PublishTarget.Find("linux-x64")!], Path.Combine(_root, "dist")));

        Assert.DoesNotContain(Assert.Single(service.StartedArguments), a => a.Contains("DirectoryBuildPropsPath", StringComparison.Ordinal));
    }

    // ---- cancelling --------------------------------------------------------

    [Fact]
    public async Task CancellingKillsThePublishProcessAndItsChildren()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs a shell that reports its child's process id.");

        var project = Write("Slow/Slow.vbproj", Project("<OutputType>Exe</OutputType>"));
        var pidFile = Path.Combine(_root, "child.pid");

        // The shell starts a grandchild and waits for it, like dotnet and its
        // MSBuild workers: killing only the shell would leave the child running.
        var service = new PublishService
        {
            Compatibility = new WindowsDesktopCompatibilitySession(_ => WindowsDesktopRetargeting.None),
            CreateStartInfo = (_, _) => new ProcessStartInfo("sh",
                ["-c", $"sleep 60 & echo $! > '{pidFile}'; wait"])
                { RedirectStandardOutput = true, RedirectStandardError = true }
        };
        using var cancellation = new CancellationTokenSource();

        var publishing = service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("linux-x64")!, PublishTarget.Find("linux-arm64")!], Path.Combine(_root, "dist")),
            cancellation.Token);

        for (var wait = 0; wait < 100 && !(File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0); wait++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        var childId = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.True(IsRunning(childId));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishing);
        for (var wait = 0; wait < 100 && IsRunning(childId); wait++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(IsRunning(childId), "The grandchild process survived the cancellation.");
        Assert.Single(service.StartedArguments);
    }

    private static bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- what the output folder holds -------------------------------------

    [Fact]
    public async Task GraphicalProgramForMacOsIsWrappedInAnAppBundle()
    {
        var project = Write("Gui/Gui.vbproj", Project("<OutputType>WinExe</OutputType><Version>2.3.4</Version><ApplicationIcon>app.icns</ApplicationIcon>"));
        Write("Gui/app.icns", "icon");
        var service = FakePublish("Gui", "appsettings.json");

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("osx-arm64")!], Path.Combine(_root, "dist"))));

        var bundle = Path.Combine(_root, "dist", "osx-arm64", "Gui.app");
        Assert.True(result.Succeeded);
        Assert.Equal(bundle, result.ArtifactPath);
        Assert.True(File.Exists(Path.Combine(bundle, "Contents", "MacOS", "Gui")));
        Assert.True(File.Exists(Path.Combine(bundle, "Contents", "MacOS", "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "Contents", "Resources", "app.icns")));
        Assert.True(File.Exists(Path.Combine(bundle, "Contents", "Info.plist")));
        Assert.False(File.Exists(Path.Combine(_root, "dist", "osx-arm64", "Gui")));
        Assert.True(result.SizeBytes > 0);

        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(bundle, "Contents", "MacOS", "Gui")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void InfoPlistNamesTheExecutableIdentifierNameVersionAndIcon()
    {
        var project = new PublishProjectInfo("/p/My App.vbproj", "My App", "2.3.4-beta.1+abc", true, "app.icns",
            RootNamespace: "Acme.Tools", Company: "Acme Corp");

        var plist = XDocument.Parse(MacAppBundle.InfoPlist(project, "My App", "app.icns"),
            LoadOptions.None);
        var values = ReadDictionary(plist);

        Assert.Equal("My App", values["CFBundleExecutable"]);
        Assert.Equal("com.acmecorp.Acme.Tools", values["CFBundleIdentifier"]);
        Assert.Equal("13.0", values["LSMinimumSystemVersion"]);
        Assert.Equal("2.3.4", values["CFBundleVersion"]);
        Assert.Equal("My App", values["CFBundleName"]);
        Assert.Equal("2.3.4", values["CFBundleShortVersionString"]);
        Assert.Equal("APPL", values["CFBundlePackageType"]);
        Assert.Equal("app.icns", values["CFBundleIconFile"]);
    }

    [Theory]
    [InlineData("1", "1.0.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("3.0.0-rc.1", "3.0.0")]
    [InlineData("nonsense", "1.0.0")]
    public void BundleVersionIsNumericMajorMinorPatch(string version, string expected) =>
        Assert.Equal(expected, MacAppBundle.NumericVersion(version));

    [Fact]
    public void BundleIdentifierFallsBackToLocalBasaltWithoutACompany()
    {
        var plain = new PublishProjectInfo("/p/My App.vbproj", "My App", "1.0.0", true, null, RootNamespace: "");

        Assert.Equal("local.basalt.MyApp", MacAppBundle.BundleIdentifier(plain));
    }

    [Fact]
    public void InfoPlistHasNoIconKeyWhenThereIsNoIcon()
    {
        var project = new PublishProjectInfo("/p/App.vbproj", "App", "1.0.0", true, null);

        var values = ReadDictionary(XDocument.Parse(MacAppBundle.InfoPlist(project, "App", null), LoadOptions.None));

        Assert.False(values.ContainsKey("CFBundleIconFile"));
    }

    private static Dictionary<string, string> ReadDictionary(XDocument plist)
    {
        var elements = plist.Root!.Element("dict")!.Elements().ToList();
        var result = new Dictionary<string, string>();
        for (var i = 0; i < elements.Count; i += 2) result[elements[i].Value] = elements[i + 1].Value;
        return result;
    }

    [Fact]
    public async Task ConsoleProgramForMacOsIsNotWrappedButRunnable()
    {
        var project = Write("Cli/Cli.vbproj", Project("<OutputType>Exe</OutputType>"));
        var service = FakePublish("Cli");

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("osx-x64")!], Path.Combine(_root, "dist"))));

        Assert.Equal(Path.Combine(_root, "dist", "osx-x64", "Cli"), result.ArtifactPath);
        Assert.False(Directory.Exists(Path.Combine(_root, "dist", "osx-x64", "Cli.app")));
    }

    [Fact]
    public async Task LinuxProgramIsMarkedExecutableAndWindowsKeepsItsExe()
    {
        var project = Write("Cli/Cli.vbproj", Project("<OutputType>Exe</OutputType>"));
        var service = FakePublish("Cli");

        var results = await service.PublishAsync(new PublishRequest(
            project, [PublishTarget.Find("linux-x64")!, PublishTarget.Find("win-x64")!], Path.Combine(_root, "dist")));

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(Path.Combine(_root, "dist", "win-x64", "Cli.exe"), results[1].ArtifactPath);

        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(results[0].ArtifactPath!).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task PublishThatLeavesNoProgramIsReportedAsAFailure()
    {
        var project = Write("Cli/Cli.vbproj", Project("<OutputType>Exe</OutputType>"));
        var service = new PublishService
        {
            Compatibility = new WindowsDesktopCompatibilitySession(_ => WindowsDesktopRetargeting.None),
            CreateStartInfo = (_, _) => Harmless()
        };

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("linux-x64")!], Path.Combine(_root, "dist"))));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Id == "BASALT0001");
    }

    [Fact]
    public async Task MacOsBundleSaysItIsUnsigned()
    {
        var project = Write("Gui/Gui.vbproj", Project("<OutputType>WinExe</OutputType>"));
        var service = FakePublish("Gui");
        var output = new List<string>();
        service.OutputReceived += (_, line) => output.Add(line);

        await service.PublishAsync(new PublishRequest(project, [PublishTarget.Find("osx-arm64")!], Path.Combine(_root, "dist")));

        Assert.Contains(output, l => l.Contains("not signed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConditionalAssemblyNameFallsBackToTheFileNameAndSaysSo()
    {
        var project = Write("Odd/Odd.vbproj", Project("<OutputType>Exe</OutputType><AssemblyName Condition=\"'$(Configuration)'=='Debug'\">Other</AssemblyName>"));
        var service = FakePublish("Odd");
        var output = new List<string>();
        service.OutputReceived += (_, line) => output.Add(line);

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("linux-x64")!], Path.Combine(_root, "dist"))));

        Assert.True(result.Succeeded);
        Assert.Contains(output, l => l.Contains("AssemblyName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailureWithoutARecognisedDiagnosticStillGivesOneProblemWithTheLastLines()
    {
        var project = Write("Bad/Bad.vbproj", Project("<OutputType>Exe</OutputType>"));
        var service = new PublishService
        {
            Compatibility = new WindowsDesktopCompatibilitySession(_ => WindowsDesktopRetargeting.None),
            CreateStartInfo = (_, _) => OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd", ["/c", "echo something odd happened & exit 3"]) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
                : new ProcessStartInfo("sh", ["-c", "echo something odd happened; exit 3"]) { RedirectStandardOutput = true, RedirectStandardError = true }
        };

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [PublishTarget.Find("linux-x64")!], Path.Combine(_root, "dist"))));

        Assert.False(result.Succeeded);
        var problem = Assert.Single(result.Diagnostics);
        Assert.Contains("3", problem.Message, StringComparison.Ordinal);
        Assert.Contains("something odd happened", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableNuGetGetsAHintInTheOutput()
    {
        var project = Write("Net/Net.vbproj", Project("<OutputType>Exe</OutputType>"));
        var service = new PublishService
        {
            Compatibility = new WindowsDesktopCompatibilitySession(_ => WindowsDesktopRetargeting.None),
            CreateStartInfo = (_, _) => OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd", ["/c", "echo error NU1101: Unable to find package & exit 1"]) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
                : new ProcessStartInfo("sh", ["-c", "echo 'error NU1101: Unable to find package'; exit 1"]) { RedirectStandardOutput = true, RedirectStandardError = true }
        };
        var output = new List<string>();
        service.OutputReceived += (_, line) => output.Add(line);

        await service.PublishAsync(new PublishRequest(project, [PublishTarget.Find("osx-arm64")!], Path.Combine(_root, "dist")));

        Assert.Contains(output, l => l.Contains("runtime pack", StringComparison.Ordinal));
    }

    // ---- the remembered choice ---------------------------------------------

    [Fact]
    public void RememberedChoiceSurvivesTheSessionFile()
    {
        var solution = Write("Sol/Sol.sln", "");
        var session = new SolutionSession
        {
            SolutionPath = solution,
            PublishRids = ["osx-arm64", "linux-x64"],
            PublishOutputRoot = "/somewhere/out"
        };

        Assert.True(SolutionUserSessionStore.Write(session));

        var read = SolutionUserSessionStore.Read(solution)!;
        Assert.Equal(["osx-arm64", "linux-x64"], read.PublishRids);
        Assert.Equal("/somewhere/out", read.PublishOutputRoot);
    }

    [AvaloniaFact]
    public void ViewModelRestoresAndCapturesTheRememberedChoice()
    {
        using var viewModel = new MainWindowViewModel();

        viewModel.RestoreMakeExecutableChoice(new SolutionSession { PublishRids = ["win-arm64"], PublishOutputRoot = "out" });

        Assert.Equal(["win-arm64"], viewModel.MakeExecutableChoice!.Rids);
        var captured = viewModel.CaptureSession();
        Assert.Equal(["win-arm64"], captured.PublishRids);
        Assert.Equal("out", captured.PublishOutputRoot);

        viewModel.RestoreMakeExecutableChoice(new SolutionSession());
        Assert.Null(viewModel.MakeExecutableChoice);
    }

    // ---- the real thing ----------------------------------------------------

    [Fact]
    public async Task TinyConsoleProjectPublishesAndRunsForTheCurrentPlatform()
    {
        var project = Write("Tiny/Tiny.vbproj", Project("<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace>"));
        Write("Tiny/Program.vb", "Module Program\n    Sub Main()\n        System.Console.Write(\"made\")\n    End Sub\nEnd Module\n");
        var service = new PublishService();
        var output = new List<string>();
        service.OutputReceived += (_, line) => { lock (output) output.Add(line); };
        var target = PublishTarget.Current();

        var result = Assert.Single(await service.PublishAsync(
            new PublishRequest(project, [target], Path.Combine(_root, "dist")),
            TestContext.Current.CancellationToken));

        // The self-contained runtime pack comes from NuGet: without it (offline
        // CI, a cold cache) the publish cannot be tried, which is not a defect.
        var log = string.Join('\n', output);
        Assert.SkipWhen(!result.Succeeded
            && (log.Contains("NU1101", StringComparison.Ordinal) || log.Contains("NU1100", StringComparison.Ordinal)
                || log.Contains("NU1301", StringComparison.Ordinal) || log.Contains("NU1102", StringComparison.Ordinal)),
            "The runtime pack for the current platform is not in the NuGet cache and the feed is unreachable.");

        Assert.True(result.Succeeded, log);
        Assert.StartsWith(Path.Combine(_root, "dist", target.Rid), result.ArtifactPath);

        using var run = Process.Start(new ProcessStartInfo(result.ArtifactPath!) { RedirectStandardOutput = true, UseShellExecute = false })!;
        var text = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await run.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("made", text);
    }
}
