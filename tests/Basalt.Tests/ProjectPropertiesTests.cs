using Basalt.Workspace.Projects;

namespace Basalt.Tests;

/// <summary>
/// Reading and writing project settings.
///
/// The file is edited in place, so anything the properties window does not
/// understand has to survive a round trip untouched.
/// </summary>
public sealed class ProjectPropertiesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-props", Guid.NewGuid().ToString("N"));

    public ProjectPropertiesTests() => Directory.CreateDirectory(_root);

    private const string SampleProject = """
        <Project Sdk="Microsoft.NET.Sdk">

          <!-- A hand-written comment that must survive editing. -->
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <OptionStrict>On</OptionStrict>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
          </ItemGroup>

        </Project>
        """;

    private ProjectProperties Load(string xml = SampleProject, string name = "App.vbproj")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, xml);
        return ProjectProperties.Load(path);
    }

    [Fact]
    public void ReadsApplicationSettings()
    {
        var properties = Load();

        Assert.Equal(ProjectOutputType.Exe, properties.OutputType);
        Assert.Equal("net10.0", properties.TargetFramework);
        Assert.Equal("App", properties.AssemblyName);
    }

    [Fact]
    public void ReadsVisualBasicCompilerOptions()
    {
        var properties = Load();

        Assert.Equal(OptionStrict.On, properties.OptionStrict);

        // Explicit and Infer default to on when the project does not say.
        Assert.True(properties.OptionExplicit);
        Assert.True(properties.OptionInfer);
        Assert.Equal(OptionCompare.Binary, properties.OptionCompare);
    }

    [Fact]
    public void WritesVisualBasicCompilerOptions()
    {
        var properties = Load();

        properties.OptionStrict = OptionStrict.Custom;
        properties.OptionInfer = false;
        properties.OptionCompare = OptionCompare.Text;

        var xml = properties.ToXml();

        Assert.Contains("<OptionStrict>Custom</OptionStrict>", xml);
        Assert.Contains("<OptionInfer>Off</OptionInfer>", xml);
        Assert.Contains("<OptionCompare>Text</OptionCompare>", xml);
    }

    [Fact]
    public void KeepsAnEmptyRootNamespaceRatherThanRemovingIt()
    {
        // In Visual Basic an empty root namespace is a deliberate setting:
        // without it the compiler prepends the project name to every namespace,
        // turning "Namespace App" into "App.App".
        var properties = Load();

        Assert.Equal("", properties.RootNamespace);

        properties.RootNamespace = "";

        Assert.Contains("<RootNamespace></RootNamespace>", properties.ToXml());
    }

    [Fact]
    public void PreservesCommentsAndUnknownContent()
    {
        var properties = Load();

        properties.AssemblyName = "Renamed";

        var xml = properties.ToXml();

        Assert.Contains("A hand-written comment that must survive editing.", xml);
        Assert.Contains("Newtonsoft.Json", xml);
    }

    [Fact]
    public void AddsAndRemovesPackageReferences()
    {
        var properties = Load();

        Assert.Single(properties.PackageReferences);

        properties.AddPackage("Serilog", "4.0.0");
        Assert.Contains(properties.PackageReferences, p => p.Id == "Serilog" && p.Version == "4.0.0");

        Assert.True(properties.RemovePackage("Serilog"));
        Assert.DoesNotContain(properties.PackageReferences, p => p.Id == "Serilog");
    }

    [Fact]
    public void UpdatesTheVersionOfAPackageAlreadyReferenced()
    {
        var properties = Load();

        properties.AddPackage("Newtonsoft.Json", "13.0.4");

        var reference = Assert.Single(properties.PackageReferences);
        Assert.Equal("13.0.4", reference.Version);
    }

    [Fact]
    public void AddsAndRemovesProjectReferences()
    {
        var properties = Load();

        properties.AddProject("../Library/Library.vbproj");
        Assert.Contains("../Library/Library.vbproj", properties.ProjectReferences);

        // Adding the same reference twice must not duplicate it.
        properties.AddProject("../Library/Library.vbproj");
        Assert.Single(properties.ProjectReferences);

        Assert.True(properties.RemoveProject("../Library/Library.vbproj"));
        Assert.Empty(properties.ProjectReferences);
    }

    [Fact]
    public void WritesAdvancedCompilationSettings()
    {
        var properties = Load();

        properties.DefineConstants = "TRACE;CUSTOM";
        properties.TreatWarningsAsErrors = true;
        properties.GenerateDocumentationFile = true;

        var xml = properties.ToXml();

        Assert.Contains("<DefineConstants>TRACE;CUSTOM</DefineConstants>", xml);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", xml);
        Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", xml);
    }

    [Fact]
    public void RemovesAPropertySetBackToItsDefault()
    {
        var properties = Load();

        properties.TreatWarningsAsErrors = true;
        properties.TreatWarningsAsErrors = false;

        Assert.DoesNotContain("TreatWarningsAsErrors", properties.ToXml());
    }

    [Fact]
    public void WritesPublishingSettings()
    {
        var properties = Load();

        properties.RuntimeIdentifier = "osx-arm64";
        properties.SelfContained = true;

        var xml = properties.ToXml();

        Assert.Contains("<RuntimeIdentifier>osx-arm64</RuntimeIdentifier>", xml);
        Assert.Contains("<SelfContained>true</SelfContained>", xml);
    }

    [Fact]
    public async Task SavesToDiskAndReadsBack()
    {
        var properties = Load();

        properties.AssemblyName = "Persisted";
        properties.OptionStrict = OptionStrict.On;
        await properties.SaveAsync();

        var reloaded = ProjectProperties.Load(properties.Path);

        Assert.Equal("Persisted", reloaded.AssemblyName);
        Assert.Equal(OptionStrict.On, reloaded.OptionStrict);
    }

    [Fact]
    public void CreatesAPropertyGroupWhenTheProjectHasNone()
    {
        var properties = Load("<Project Sdk=\"Microsoft.NET.Sdk\" />", "Bare.vbproj");

        properties.AssemblyName = "Bare";

        Assert.Contains("<AssemblyName>Bare</AssemblyName>", properties.ToXml());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>Run and debug settings, stored beside the project.</summary>
public sealed class LaunchSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-launch", Guid.NewGuid().ToString("N"));

    public LaunchSettingsTests() => Directory.CreateDirectory(_root);

    private string CreateProject(string name = "App.vbproj")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return path;
    }

    [Fact]
    public void StartsEmptyWhenNoSettingsFileExists()
    {
        var settings = LaunchSettings.Load(CreateProject());

        Assert.Equal("App", settings.ProfileName);
        Assert.Null(settings.CommandLineArguments);
    }

    [Fact]
    public async Task WritesAndReadsBackRunSettings()
    {
        var project = CreateProject();
        var settings = LaunchSettings.Load(project);

        settings.CommandLineArguments = "--verbose input.txt";
        settings.WorkingDirectory = "/tmp";
        settings.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        await settings.SaveAsync();

        var reloaded = LaunchSettings.Load(project);

        Assert.Equal("--verbose input.txt", reloaded.CommandLineArguments);
        Assert.Equal("/tmp", reloaded.WorkingDirectory);
        Assert.Equal("Development", reloaded.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public async Task RemovesAnEnvironmentVariableSetToNull()
    {
        var project = CreateProject();
        var settings = LaunchSettings.Load(project);

        settings.SetEnvironmentVariable("TEMPORARY", "1");
        settings.SetEnvironmentVariable("TEMPORARY", null);
        await settings.SaveAsync();

        var reloaded = LaunchSettings.Load(project);

        Assert.DoesNotContain("TEMPORARY", reloaded.EnvironmentVariables.Keys);
    }

    [Fact]
    public void SurvivesASettingsFileThatNoLongerParses()
    {
        // A hand-edited file must not stop the project from opening.
        var project = CreateProject();
        var properties = Path.Combine(_root, "Properties");
        Directory.CreateDirectory(properties);
        File.WriteAllText(Path.Combine(properties, "launchSettings.json"), "{ not json");

        var settings = LaunchSettings.Load(project);

        Assert.NotNull(settings);
        Assert.Null(settings.CommandLineArguments);
    }

    [Fact]
    public async Task KeepsTheExistingProfileName()
    {
        var project = CreateProject();
        var properties = Path.Combine(_root, "Properties");
        Directory.CreateDirectory(properties);

        await File.WriteAllTextAsync(Path.Combine(properties, "launchSettings.json"), """
            {
              "profiles": {
                "Custom Profile": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:7000"
                }
              }
            }
            """);

        var settings = LaunchSettings.Load(project);

        Assert.Equal("Custom Profile", settings.ProfileName);
        Assert.Equal("http://localhost:7000", settings.ApplicationUrl);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
