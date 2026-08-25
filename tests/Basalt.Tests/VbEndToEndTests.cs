using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Verifies the complete path required of the IDE: create an Avalonia window
/// from the designer, edit it visually, generate the VB.NET code-behind and
/// build the resulting project.
/// </summary>
public sealed class FlussoCompletoVbTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-e2e", Guid.NewGuid().ToString("N"));

    public FlussoCompletoVbTests() => Directory.CreateDirectory(_root);

    private string CreateVbAvaloniaProject()
    {
        var projectPath = Path.Combine(_root, "AppProva.vbproj");

        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
              </ItemGroup>
            </Project>
            """);

        return projectPath;
    }

    [Fact]
    public async Task CreaModificaECompilaUnaFinestraVisualBasic()
    {
        var projectPath = CreateVbAvaloniaProject();

        // 1. Creating the window from the template, as "Add window" would do.
        var created = await FormTemplates.CreateWindowAsync(
            _root, "AppProva.FinestraCliente", SourceLanguage.VisualBasic, "Scheda cliente");

        Assert.True(File.Exists(created.XamlPath));
        Assert.True(File.Exists(created.CodeBehindPath));
        Assert.EndsWith(".axaml.vb", created.CodeBehindPath);

        // 2. Visual editing: two controls are dragged in and their names are set.
        var session = new DesignerSession(
            XamlDocument.Load(created.XamlPath), SourceLanguage.VisualBasic);

        var grid = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(grid);

        var stack = session.InsertFromToolbox(
            ToolboxCatalog.Items.First(i => i.ElementName == "StackPanel"));
        session.Select(stack);

        var casella = session.InsertFromToolbox(
            ToolboxCatalog.Items.First(i => i.ElementName == "TextBox"));
        session.SetName("CasellaNome");

        session.Select(stack);
        session.InsertFromToolbox(ToolboxCatalog.Items.First(i => i.ElementName == "Button"));
        session.SetName("PulsanteSalva");
        session.SetProperty("Content", "Salva");

        await session.Document.SaveAsync();

        // 3. The saved XAML contains the designer's changes.
        var xaml = await File.ReadAllTextAsync(created.XamlPath);
        Assert.Contains("""x:Name="CasellaNome" """.TrimEnd(), xaml);
        Assert.Contains("""x:Name="PulsanteSalva" """.TrimEnd(), xaml);
        Assert.Contains("""Content="Salva" """.TrimEnd(), xaml);
        Assert.Contains("x:Class=\"AppProva.FinestraCliente\"", xaml);

        // 4. The project builds: XAML and VB code-behind are consistent.
        var build = await new MsBuildBuildService().BuildAsync(projectPath);

        var errori = build.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.True(build.Succeeded, $"Compilazione non riuscita:\n{string.Join('\n', errori)}");
        Assert.NotNull(build.OutputAssemblyPath);
    }

    [Fact]
    public async Task NonSovrascriveUnaFinestraEsistente()
    {
        await FormTemplates.CreateWindowAsync(_root, "App.Doppia", SourceLanguage.VisualBasic);

        await Assert.ThrowsAsync<IOException>(() =>
            FormTemplates.CreateWindowAsync(_root, "App.Doppia", SourceLanguage.VisualBasic));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* build still running: irrelevant */ }
    }
}
