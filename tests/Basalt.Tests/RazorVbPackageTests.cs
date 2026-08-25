using System.Diagnostics;

namespace Basalt.Tests;

/// <summary>
/// The NuGet package, used the way someone outside this repository would.
///
/// A project is built that references nothing but the package: no project
/// references, no source shared with the IDE. That is the only way to catch
/// packaging faults, which are invisible from inside the solution — the
/// targets file that broke every consuming build compiled perfectly here.
/// </summary>
[Collection("PackageBuild")]
public sealed class RazorVbPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-package", Guid.NewGuid().ToString("N"));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                directory = directory.Parent;

            return directory?.FullName ?? "";
        }
    }

    public RazorVbPackageTests() => Directory.CreateDirectory(_root);

    /// <summary>Runs a command, returning what it printed and how it ended.</summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(
        string command, string arguments, string workingDirectory)
    {
        var process = Process.Start(new ProcessStartInfo(command)
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, output + error);
    }

    [Fact]
    public async Task AProjectReferencingOnlyThePackageBuildsAndRenders()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");

        var packages = Path.Combine(_root, "packages");
        var project = Path.Combine(_root, "Consumer");

        Directory.CreateDirectory(packages);
        Directory.CreateDirectory(Path.Combine(project, "Views"));

        // Packed here rather than reusing an earlier package, so the test
        // covers what this working tree would actually publish.
        var packed = await RunAsync("dotnet",
            $"pack \"{Path.Combine(RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "Basalt.Razor.Vb.Generator.csproj")}\" "
          + $"-c Release -o \"{packages}\" --nologo",
            RepositoryRoot);

        Assert.True(packed.ExitCode == 0, packed.Output);

        await File.WriteAllTextAsync(Path.Combine(project, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{packages}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Consumer.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Views", "Hello.vbhtml"), """
            @ModelType String

            <h1>Hello, @Model</h1>
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Program.vb"), """
            Module Program
                Sub Main()
                    Dim view As New Views.Hello()
                    view.Model = "world"
                    Console.WriteLine(view.Render())
                End Sub
            End Module
            """);

        // A package folder left from another run would hide a packaging fault.
        var cached = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "basalt.razor.vb");

        try { if (Directory.Exists(cached)) Directory.Delete(cached, recursive: true); }
        catch (IOException) { }

        var built = await RunAsync("dotnet", "build -c Release --nologo", project);

        Assert.True(built.ExitCode == 0, built.Output);

        var ran = await RunAsync("dotnet", "run -c Release --no-build", project);

        Assert.True(ran.ExitCode == 0, ran.Output);

        // The point of the whole package: Visual Basic Razor, which ASP.NET
        // Core cannot compile at all.
        Assert.Contains("<h1>Hello, world</h1>", ran.Output);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Package tests run one at a time.
///
/// They clear the shared NuGet cache entry, so two running together would
/// delete the package the other is building against.
/// </summary>
[CollectionDefinition("PackageBuild", DisableParallelization = true)]
public sealed class PackageBuildCollection;
