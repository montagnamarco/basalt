using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Basalt.Tests;

/// <summary>
/// A Blazor component with a scoped .vbrazor.css, built through a symlinked
/// project path, the way the IDE builds so an edited file and the one on disk
/// are the same inode.
/// </summary>
/// <remarks>
/// The CSS-isolation target in Basalt.Razor.Vb.targets removes every .vbrazor
/// that has a matching .vbrazor.css from AdditionalFiles and re-adds it built
/// from the .vbrazor.css item, to attach the CssScope metadata the component
/// generator needs. The re-added item used to lose BasaltRelativePath along
/// the way: fine through a direct path, where the fallback comparison against
/// BasaltProjectDir still agreed, but wrong through a symlink, where
/// BasaltProjectDir is the project directory as given (through the symlink)
/// and the AdditionalFiles path is resolved against the physical directory
/// the compiler is really running in. MainLayout.vbrazor, in Components/Layout,
/// was namespaced plain "Components" instead of "Components.Layout" — the
/// same regression the Blazor template's MainLayout and NavMenu would hit.
///
/// Reproducing the divergence needs the build invoked with a working
/// directory other than the symlink itself: cd-ing into the symlink first
/// resolves the process's own working directory to the physical one before
/// MSBuild ever sees it (confirmed by comparing BasaltProjectDir in the
/// generated analyzer config file against the AdditionalFiles path reported
/// for the same build), which left both sides agreeing anyway and the
/// defect sitting invisible either way until a solution reachable only
/// through the symlink from a build is started elsewhere, the way the IDE's
/// own working directory sits outside the checkout.
/// </remarks>
[Collection("PackageBuild")]
public sealed class BlazorCssIsolationSymlinkBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-symlink-blazor", Guid.NewGuid().ToString("N"));

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

    public BlazorCssIsolationSymlinkBuildTests() => Directory.CreateDirectory(_root);

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
    public async Task AComponentWithScopedCssIsNamespacedByItsFolderThroughASymlinkedProjectPath()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");
        // Directory.CreateSymbolicLink needs a privilege Windows does not
        // grant by default; the fix is proved on macOS and Linux instead.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symlinks need elevation on Windows.");

        var generatorProject = Path.Combine(
            RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "Basalt.Razor.Vb.Generator.csproj");
        var targetsFile = Path.Combine(
            RepositoryRoot, "src", "Basalt.Razor.Vb.Generator", "build", "Basalt.Razor.Vb.targets");

        // Built once, directly, rather than through BuildProjectReferences on
        // the symlinked project below: that would rebuild the generator from
        // inside the symlinked working directory and, on a project whose
        // *build inputs* never changed, MSBuild can decide there is nothing
        // to do and serve the stale generator from an earlier run instead.
        var generatorBuild = await RunAsync("dotnet",
            $"build \"{generatorProject}\" -c Release --nologo", RepositoryRoot);
        Assert.True(generatorBuild.ExitCode == 0, generatorBuild.Output);

        var real = Path.Combine(_root, "Real");
        var link = Path.Combine(_root, "Link");

        Directory.CreateDirectory(Path.Combine(real, "Components", "Layout"));

        await File.WriteAllTextAsync(Path.Combine(real, "SymlinkBlazor.vbproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>SymlinkBlazor</RootNamespace>
                <IsPackable>false</IsPackable>
                <BasaltProjectDir>$(MSBuildProjectDirectory)</BasaltProjectDir>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{generatorProject}"
                                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <ItemGroup>
                <CompilerVisibleProperty Include="BasaltProjectDir" />
                <AdditionalFiles Include="Components/**/*.vbrazor" />
                <AdditionalFiles Update="Components/**/*.vbrazor">
                  <BasaltRelativePath Condition="'%(BasaltRelativePath)' == '' and !$([System.IO.Path]::IsPathRooted('%(Identity)'))">%(Identity)</BasaltRelativePath>
                  <BasaltRelativePath Condition="'%(BasaltRelativePath)' == '' and $([System.IO.Path]::IsPathRooted('%(Identity)'))">$([MSBuild]::MakeRelative($(MSBuildProjectDirectory), %(FullPath)))</BasaltRelativePath>
                </AdditionalFiles>
                <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="BasaltRelativePath" />
              </ItemGroup>
              <Import Project="{targetsFile}" />
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(real, "Program.vb"), """
            Module Program
                Sub Main(args As String())
                    Dim builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args)
                    Dim app = builder.Build()
                End Sub
            End Module
            """);

        await File.WriteAllTextAsync(Path.Combine(real, "Components", "Layout", "MainLayout.vbrazor"), """
            @Inherits Microsoft.AspNetCore.Components.LayoutComponentBase
            <div class="layout">
                @Body
            </div>
            """);

        // The scoped stylesheet: without it ScopedCssEnabled's pipeline, and
        // so _BasaltComputeVbRazorCssScopes, never runs at all, and the
        // regression this test is for cannot occur.
        await File.WriteAllTextAsync(Path.Combine(real, "Components", "Layout", "MainLayout.vbrazor.css"), """
            .layout {
                display: flex;
            }
            """);

        Directory.CreateSymbolicLink(link, real);

        // Built from the repository root, not from inside the symlink: cd-ing
        // into the symlink first would let the shell's own working-directory
        // resolution settle on the physical folder before MSBuild ever runs,
        // which leaves BasaltProjectDir and the AdditionalFiles path agreeing
        // by accident and proves nothing.
        var build = await RunAsync("dotnet",
            $"build \"{Path.Combine(link, "SymlinkBlazor.vbproj")}\" -c Release --nologo "
          + "-p:BuildProjectReferences=false", RepositoryRoot);

        Assert.True(build.ExitCode == 0, build.Output);

        var assembly = Path.Combine(real, "bin", "Release", "net10.0", "SymlinkBlazor.dll");
        using var stream = File.OpenRead(assembly);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        var component = Assert.Single(metadata.TypeDefinitions, handle =>
            metadata.GetString(metadata.GetTypeDefinition(handle).Name) == "MainLayout");

        // Components.Layout, as a .razor file in the same folder would be
        // namespaced, not plain Components: namespacing by folder is what the
        // BasaltRelativePath metadata exists to keep correct, symlink or not.
        Assert.Equal("SymlinkBlazor.Components.Layout",
            metadata.GetString(metadata.GetTypeDefinition(component).Namespace));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
