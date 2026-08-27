using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Generated solutions must build: a template that produces invalid code is
/// worse than not having one at all.
/// </summary>
public sealed class SolutionTemplatesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-sln", Guid.NewGuid().ToString("N"));

    public SolutionTemplatesTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.WebApi)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.WebApp)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.AvaloniaApp)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.ConsoleApp)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.ClassLibrary)]
    public async Task LaSoluzioneGenerataCompila(SourceLanguage language, ProjectTemplate template)
    {
        var name = $"Prova{language}{template}";
        var created = await SolutionTemplates.CreateAsync(_root, name, template);

        Assert.True(File.Exists(created.SolutionPath));
        Assert.True(File.Exists(created.ProjectPath));

        var build = await new MsBuildBuildService().BuildAsync(created.ProjectPath);

        var errori = build.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        Assert.True(build.Succeeded, $"Compilazione non riuscita:\n{string.Join('\n', errori)}");
    }

    [Theory]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.WebApi)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.RazorPages)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.Mvc)]
    [InlineData(SourceLanguage.VisualBasic, ProjectTemplate.WebApp)]
    public async Task IWebTemplateProduconoIFileDiSupporto(
        SourceLanguage language, ProjectTemplate template)
    {
        // Every web project needs configuration and launch settings to run.
        var name = $"Support{language}{template}";
        var created = await SolutionTemplates.CreateAsync(_root, name, template);

        var directory = Path.GetDirectoryName(created.ProjectPath)!;

        Assert.True(File.Exists(Path.Combine(directory, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(directory, "Properties", "launchSettings.json")));
        Assert.True(Directory.Exists(Path.Combine(directory, "wwwroot")));
    }

    [Theory]
    [InlineData(ProjectTemplate.WebApi)]
    [InlineData(ProjectTemplate.RazorPages)]
    [InlineData(ProjectTemplate.Mvc)]
    [InlineData(ProjectTemplate.WebApp)]
    public async Task IProgettiWebUsanoIlSdkWeb(ProjectTemplate template)
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, $"Sdk{template}", template);

        var project = await File.ReadAllTextAsync(created.ProjectPath);

        Assert.Contains("Microsoft.NET.Sdk.Web", project);

        // The Web SDK infers the output type; declaring it fights the SDK.
        Assert.DoesNotContain("<OutputType>", project);
    }

    [Theory]
    [InlineData(ProjectTemplate.RazorPages)]
    [InlineData(ProjectTemplate.Mvc)]
    public async Task IProgettiVbConViewCollegatoIlGeneratoreVbHtml(ProjectTemplate template)
    {
        // Razor emits C# only, so a Visual Basic project needs our generator
        // to compile its templates.
        var created = await SolutionTemplates.CreateAsync(
            _root, $"VbViews{template}", template);

        var project = await File.ReadAllTextAsync(created.ProjectPath);

        Assert.Contains("Basalt.Razor.Vb.Generator", project);
        Assert.Contains("OutputItemType=\"Analyzer\"", project);
        Assert.Contains("AdditionalFiles", project);

        var directory = Path.GetDirectoryName(created.ProjectPath)!;
        Assert.NotEmpty(Directory.GetFiles(directory, "*.vbhtml", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task IlTemplateAvaloniaProduceUnaFinestraApribileNelDesigner()
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, "AppConFinestra", ProjectTemplate.AvaloniaApp);

        Assert.NotNull(created.MainWindowXamlPath);
        Assert.True(File.Exists(created.MainWindowXamlPath));

        // The code-behind must be VB, like the project.
        Assert.True(File.Exists($"{created.MainWindowXamlPath}.vb"));

        var document = Basalt.Designer.Model.XamlDocument.Load(created.MainWindowXamlPath!);
        Assert.Equal("AppConFinestra.MainWindow", document.ClassName);
    }

    [Fact]
    public async Task RifiutaUnaCartellaGiaPopolata()
    {
        await SolutionTemplates.CreateAsync(
            _root, "Doppia", ProjectTemplate.ClassLibrary);

        await Assert.ThrowsAsync<IOException>(() => SolutionTemplates.CreateAsync(
            _root, "Doppia", ProjectTemplate.ClassLibrary));
    }

    [Fact]
    public async Task IlFileSolutionRiferisceIlProgettoConIlGuidDelLinguaggio()
    {
        var vb = await SolutionTemplates.CreateAsync(
            _root, "SlnVb", ProjectTemplate.ClassLibrary);

        var contenuto = await File.ReadAllTextAsync(vb.SolutionPath);

        // VB.NET project type GUID as defined by Visual Studio.
        Assert.Contains("F184B08F-C81C-45F6-A57F-5ABD9991F28F", contenuto);
        Assert.Contains("SlnVb.vbproj", contenuto);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task TheVisualBasic6TemplateWritesWhatVisualBasic6Wrote()
    {
        // A .vbp and a .frm, not a .vbproj: the point of this template is a
        // project that opens both here and in Visual Basic 6, and Basalt
        // writes the .NET project beside it when the .vbp is opened.
        var result = await SolutionTemplates.CreateAsync(
            _root, "Anagrafica", ProjectTemplate.VisualBasic6);

        Assert.EndsWith(".vbp", result.ProjectPath);

        var directory = Path.GetDirectoryName(result.ProjectPath)!;
        var form = File.ReadAllText(Path.Combine(directory, "Form1.frm"));

        // CRLF, because that is what a .frm is. A file with Unix endings is
        // one no copy of Visual Basic 6 ever produced.
        Assert.Contains("\r\n", form);

        // And it reads back as a form, which is the only thing that matters:
        // a template that writes something the parser cannot read is a
        // template that produces nothing.
        var parsed = Basalt.Vb6.FormFile.Parse(form);

        Assert.Contains(parsed.Root.Children, c => c.Name == "cmdSaluta");
        Assert.Contains("cmdSaluta_Click", parsed.Code);
    }

    [Fact]
    public async Task TheBlazorTemplateWritesAComponentAndHostsIt()
    {
        var result = await SolutionTemplates.CreateAsync(
            _root, "Sito", ProjectTemplate.Blazor);

        var directory = Path.GetDirectoryName(result.ProjectPath)!;

        Assert.True(File.Exists(Path.Combine(directory, "Components", "Home.vbrazor")));

        var project = File.ReadAllText(result.ProjectPath);

        // Declared in the project rather than left to the package's props:
        // those are imported for a PackageReference and not for a project
        // one, so a .vbrazor reached the compiler as nothing at all and the
        // component was never generated.
        Assert.Contains("*.vbrazor", project);

        var program = File.ReadAllText(Path.Combine(directory, "Program.vb"));

        // Without the root namespace, which the project deliberately leaves
        // empty: Visual Basic prepends RootNamespace to every Namespace
        // statement, so naming it here would look for Sito.Sito.Components.
        Assert.Contains("Global.Components.Home", program);

        // And the antiforgery middleware, without which a component answers
        // 500 and the error names middleware rather than the page.
        Assert.Contains("UseAntiforgery", program);
    }
}
