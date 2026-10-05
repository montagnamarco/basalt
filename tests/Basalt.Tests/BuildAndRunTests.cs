using Avalonia.Headless.XUnit;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Getting from a project to something that can be run or debugged.
///
/// "The build produced no assembly" was said for three different reasons — no
/// project, a build that failed, a build that produced nothing — and only one
/// of them is about an assembly. It also only ever looked in bin/Debug, so
/// choosing Release found the Debug output or nothing at all.
/// </summary>
public sealed class BuildAndRunTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-buildrun-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string WriteProject(string body = "", string name = "Probe")
    {
        var path = Path.Combine(_root, $"{name}.vbproj");

        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_root, "Program.vb"),
            body.Length > 0
                ? body
                : "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        return path;
    }

    [AvaloniaFact]
    public void ProcessExitReplacesTheRunningStatusWithTheExitCode()
    {
        using var viewModel = new MainWindowViewModel();
        viewModel.SetRunningForTests(true);
        viewModel.StatusMessage = "Running";
        viewModel.ReportRunExitedForTests(7);
        Assert.False(viewModel.IsRunning);
        Assert.Equal(Basalt.Core.Localization.Localizer.Get("Status_RunExited", 7), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public void StopRunKeepsTheStoppedStatusWhenTheKilledProcessExitArrivesLate()
    {
        using var viewModel = new MainWindowViewModel();
        var generation = viewModel.StartRunGenerationForTests();

        viewModel.StopRunCommand.Execute(null);
        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.StatusStopped),
            viewModel.StatusMessage);

        // Process.Exited is asynchronous: the kill Stop asked for can still
        // report in after the status bar already says "stopped".
        viewModel.ReportRunExitedForTests(137, generation);

        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.StatusStopped),
            viewModel.StatusMessage);
        Assert.False(viewModel.IsRunning);
    }

    [AvaloniaFact]
    public void LateExitFromAPreviousRunDoesNotOverwriteTheCurrentStatus()
    {
        using var viewModel = new MainWindowViewModel();
        var previousGeneration = viewModel.StartRunGenerationForTests();

        // A newer run is already the one anybody cares about.
        viewModel.StartRunGenerationForTests();
        viewModel.StatusMessage = "Building the next run";

        viewModel.ReportRunExitedForTests(99, previousGeneration);

        Assert.Equal("Building the next run", viewModel.StatusMessage);
        Assert.True(viewModel.IsRunning);
    }

    [AvaloniaFact]
    public async Task RunWhileAlreadyRunningReportsStatusAlreadyRunning()
    {
        var marker = Path.Combine(_root, "already-running.txt");
        var project = WriteProject("""
            Module Program
                Sub Main()
                    System.IO.File.WriteAllText("already-running.txt", "started")
                    System.Threading.Thread.Sleep(5000)
                End Sub
            End Module
            """);
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(project);

        await viewModel.RunCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 100 && !File.Exists(marker); attempt++)
        {
            await Task.Delay(30);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.True(File.Exists(marker));

        // The first run is still going: asking to run again must say so
        // rather than silently doing nothing or starting a second process,
        // and must say so before building: a build would clear the Problems
        // list (and on Windows fail on the locked output).
        viewModel.Diagnostics.Add(new Basalt.Core.Model.IdeDiagnostic(
            "X1", "stale", Basalt.Core.Model.DiagnosticSeverity.Warning, "Program.vb", 1, 1));
        viewModel.BuildOutput.Add("kept");
        await viewModel.RunCommand.ExecuteAsync(null);
        Assert.Contains(viewModel.Diagnostics, d => d.Message == "stale");
        Assert.Contains("kept", viewModel.BuildOutput);

        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.StatusAlreadyRunning),
            viewModel.StatusMessage);

        viewModel.StopRunCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task AProjectThatBuildsGivesAnAssembly()
    {
        var vm = new MainWindowViewModel();

        await vm.OpenSolutionAsync(WriteProject());

        var assembly = await vm.BuildForDebuggingAsync();

        Assert.NotNull(assembly);
        Assert.True(File.Exists(assembly));
    }

    [AvaloniaFact]
    public async Task ChoosingReleaseLooksInTheReleaseFolder()
    {
        var vm = new MainWindowViewModel();

        await vm.OpenSolutionAsync(WriteProject());

        vm.Configuration = "Release";

        var assembly = await vm.BuildForDebuggingAsync();

        Assert.NotNull(assembly);
        Assert.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            assembly, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ABuildThatFailsGivesNothingAndSaysWhy()
    {
        var vm = new MainWindowViewModel();

        await vm.OpenSolutionAsync(WriteProject(
            "Module Program\n    Sub Main()\n        NoSuchMethod()\n    End Sub\nEnd Module\n"));

        var assembly = await vm.BuildForDebuggingAsync();

        Assert.Null(assembly);

        // The reason is in the problems, not swallowed.
        Assert.Contains(vm.Diagnostics,
            d => d.Severity == Basalt.Core.Model.DiagnosticSeverity.Error);
    }

    [AvaloniaFact]
    public async Task ItFindsAnAssemblyNamedDifferentlyFromItsProject()
    {
        // AssemblyName need not match the project file, and looking only for
        // "{project}.dll" then found nothing at all.
        var path = Path.Combine(_root, "Probe.vbproj");

        await File.WriteAllTextAsync(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <AssemblyName>SomethingElse</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        var vm = new MainWindowViewModel();

        await vm.OpenSolutionAsync(path);

        var assembly = await vm.BuildForDebuggingAsync();

        Assert.NotNull(assembly);
        Assert.EndsWith("SomethingElse.dll", assembly, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task RenamingTheAssemblyDoesNotDebugThePreviousOutput()
    {
        var path = Path.Combine(_root, "Probe.vbproj");
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(path, project);
        await File.WriteAllTextAsync(Path.Combine(_root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(path);
        var original = await viewModel.BuildForDebuggingAsync();
        Assert.NotNull(original);
        Assert.EndsWith("Probe.dll", original, StringComparison.Ordinal);

        // Keep a previous output as a custom build target or interrupted cleanup can.
        File.Copy(original, Path.Combine(_root, "Stale.dll"));
        var renamedProject = project.Replace("</PropertyGroup>",
            "<AssemblyName>Renamed</AssemblyName></PropertyGroup>", StringComparison.Ordinal)
            .Replace("</Project>", """
                <Target Name="RetainPreviousAssembly" AfterTargets="Build">
                  <Copy SourceFiles="$(MSBuildProjectDirectory)/Stale.dll" DestinationFiles="$(TargetDir)Probe.dll" />
                </Target>
                </Project>
                """, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, renamedProject);
        var renamed = await viewModel.BuildForDebuggingAsync();
        Assert.True(File.Exists(original));
        Assert.NotNull(renamed);
        Assert.EndsWith("Renamed.dll", renamed, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task WithNoProjectItSaysSo()
    {
        var vm = new MainWindowViewModel();

        Assert.Null(await vm.BuildForDebuggingAsync());
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [AvaloniaFact]
    public async Task RunCommandLaunchesTheBuiltApplication()
    {
        var marker = Path.Combine(_root, "started.txt");
        var project = WriteProject("Module Program\n    Sub Main()\n        System.IO.File.WriteAllText(\"started.txt\", \"started\")\n    End Sub\nEnd Module\n");
        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(project);
        await vm.RunCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 100 && !File.Exists(marker); attempt++)
        {
            await Task.Delay(30);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.True(File.Exists(marker), string.Join(Environment.NewLine, vm.BuildOutput));
        Assert.Equal("started", await File.ReadAllTextAsync(marker));
        // Play runs the project through "dotnet run --no-build": that keeps
        // launchSettings.json (environment, arguments, URLs) and does not
        // compile a second time after the build Play just did.
        var arguments = vm.LastLaunchArgumentsForTests.ToList();
        Assert.Equal("run", arguments[0]);
        Assert.Equal(project, arguments[arguments.IndexOf("--project") + 1]);
        Assert.Contains("--no-build", arguments);
    }

    [AvaloniaFact]
    public async Task PlayButtonStartsTheApplicationWithOrWithoutAnInstalledDebugger()
    {
        var marker = Path.Combine(_root, "play-started.txt");
        var project = WriteProject("Module Program\n    Sub Main()\n        System.IO.File.WriteAllText(\"play-started.txt\", \"started\")\n    End Sub\nEnd Module\n");
        using var host = new TestWindow();
        var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
        await vm.OpenSolutionAsync(project);
        var start = typeof(Basalt.Shell.MainWindow).GetMethod("StartOrContinueDebuggingAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await Assert.IsAssignableFrom<Task>(start.Invoke(host.Window, null));
        for (var attempt = 0; attempt < 150 && !File.Exists(marker); attempt++)
        {
            await Task.Delay(30);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.True(File.Exists(marker), string.Join(Environment.NewLine, vm.BuildOutput));
    }

    [AvaloniaFact]
    public async Task SaveFailureIsReportedAndDoesNotStartDebugging()
    {
        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(WriteProject());
        var directory = Path.Combine(_root, "not-a-document.vb");
        Directory.CreateDirectory(directory);
        var document = new EditorDocumentViewModel(directory, "before");
        document.Text = "after";
        vm.OpenDocuments.Add(document);
        Assert.Null(await vm.BuildForDebuggingAsync());
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsRunning);
        Assert.Contains("interrupted", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingLaunchTargetDuringBuildDoesNotUseAnotherOutput(bool changeConfiguration)
    {
        var first = WriteProject(name: "First");
        var second = WriteProject(name: "Second");
        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(second);
        Assert.NotNull(await vm.BuildForDebuggingAsync());
        vm.SetStartupProject(first);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.IsBusy) || !vm.IsBusy) return;
            if (changeConfiguration) vm.Configuration = "Release";
            else vm.SetStartupProject(second);
        };
        Assert.Null(await vm.BuildForDebuggingAsync());
        Assert.True(File.Exists(Path.Combine(_root, "bin", "Debug", "net10.0", "First.dll")));
        Assert.False(vm.IsRunning);
        Assert.Contains("changed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
