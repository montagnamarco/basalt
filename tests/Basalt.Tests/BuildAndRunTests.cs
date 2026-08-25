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
    public async Task WithNoProjectItSaysSo()
    {
        var vm = new MainWindowViewModel();

        Assert.Null(await vm.BuildForDebuggingAsync());
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }
}
