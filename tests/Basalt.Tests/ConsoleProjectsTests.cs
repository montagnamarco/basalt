using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The rules that decide whether a project is a console program. A console program runs in a terminal tab; any
/// other project keeps the redirected run, so each rule is a case where the answer must be "no".
/// </summary>
public sealed class ConsoleProjectsTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Writes a project file in a folder of its own and returns its path.</summary>
    private string WriteProject(string xml)
    {
        var folder = Directory.CreateTempSubdirectory("basalt-console-rules-").FullName;
        _folders.Add(folder);

        var path = Path.Combine(folder, "App.vbproj");
        File.WriteAllText(path, xml);
        return path;
    }

    /// <summary>A project on the plain SDK, with the given properties and items inside it.</summary>
    private static string Project(
        string properties = "<OutputType>Exe</OutputType>",
        string sdk = "Microsoft.NET.Sdk",
        string items = "") => $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            {properties}
          </PropertyGroup>
          {items}
        </Project>
        """;

    [Fact]
    public void AnExecutableOnThePlainSdkIsAConsoleProgram()
    {
        Assert.True(ConsoleProjects.IsConsole(WriteProject(Project())));
    }

    [Fact]
    public void ALibraryIsNotAConsoleProgram()
    {
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project("<OutputType>Library</OutputType>"))));
    }

    [Fact]
    public void AProjectWithNoOutputTypeIsNotAConsoleProgram()
    {
        // MSBuild defaults a plain project to a library, so nothing says it runs.
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project(""))));
    }

    [Fact]
    public void AWindowsExecutableIsNotAConsoleProgram()
    {
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project("<OutputType>WinExe</OutputType>"))));
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Web")]
    [InlineData("Microsoft.NET.Sdk.Razor")]
    [InlineData("Microsoft.NET.Sdk.BlazorWebAssembly")]
    public void AWebOrRazorProjectIsNotAConsoleProgram(string sdk)
    {
        // These are executables too, but they serve pages: a console window would show none of what they do.
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project(sdk: sdk))));
    }

    [Fact]
    public void AProjectWithoutAnSdkAttributeIsNotAConsoleProgram()
    {
        var xml = """
            <Project>
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """;

        Assert.False(ConsoleProjects.IsConsole(WriteProject(xml)));
    }

    [Fact]
    public void AProjectWithWindowsFormsIsNotAConsoleProgram()
    {
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project(
            "<OutputType>Exe</OutputType><UseWindowsForms>true</UseWindowsForms>"))));
    }

    [Fact]
    public void AProjectWithWpfIsNotAConsoleProgram()
    {
        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project(
            "<OutputType>Exe</OutputType><UseWPF>True</UseWPF>"))));
    }

    [Fact]
    public void AProjectThatSwitchesWindowsFormsOffIsAConsoleProgram()
    {
        // Only "true" counts: a project that says false is not a Forms project.
        Assert.True(ConsoleProjects.IsConsole(WriteProject(Project(
            "<OutputType>Exe</OutputType><UseWindowsForms>false</UseWindowsForms><UseWPF>false</UseWPF>"))));
    }

    [Theory]
    [InlineData("Avalonia")]
    [InlineData("Avalonia.Desktop")]
    [InlineData("avalonia.desktop")]
    public void AProjectReferencingAvaloniaIsNotAConsoleProgram(string package)
    {
        // A project that draws with Avalonia opens a window, whatever the output type says.
        var items = $"""
            <ItemGroup>
              <PackageReference Include="{package}" Version="12.1.1" />
            </ItemGroup>
            """;

        Assert.False(ConsoleProjects.IsConsole(WriteProject(Project(items: items))));
    }

    [Fact]
    public void AProjectReferencingAnUnrelatedPackageIsAConsoleProgram()
    {
        var items = """
            <ItemGroup>
              <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            </ItemGroup>
            """;

        Assert.True(ConsoleProjects.IsConsole(WriteProject(Project(items: items))));
    }

    [Fact]
    public void AProjectThatCannotBeReadIsNotAConsoleProgram()
    {
        // Nothing can be said about it, so it keeps the run it always had.
        var missing = Path.Combine(Path.GetTempPath(), "basalt-no-such-" + Guid.NewGuid().ToString("N"), "App.vbproj");

        Assert.False(ConsoleProjects.IsConsole(missing));
    }

    [Fact]
    public void AProjectThatIsNotXmlIsNotAConsoleProgram()
    {
        Assert.False(ConsoleProjects.IsConsole(WriteProject("this is not a project file <<")));
    }
}
