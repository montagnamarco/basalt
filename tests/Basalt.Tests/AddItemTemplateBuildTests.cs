using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Classic;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// What every Add New Item template writes is what its project kind accepts:
/// the Visual Basic compiles with Roslyn against the references of that kind,
/// the Razor templates go through the parser the compiler uses (and the real
/// generators), the Windows Forms pairs open in the designer's document, and
/// the data files are what they claim to be.
/// </summary>
public sealed class AddItemTemplateBuildTests
{
    private static bool Reserved(string name) => ExplorerFileOperations.IsVisualBasicReservedWord(name);

    private static ItemTemplateDefinition Template(string id) => ItemTemplates.ById(id);

    /// <summary>The files a template writes under the name it suggests.</summary>
    private static IReadOnlyList<ItemFile> Generate(string id, string? typed = null)
    {
        var template = Template(id);
        var result = ItemTemplates.Check(template, typed ?? template.DefaultName, Reserved);
        Assert.True(result.IsValid, $"{id}: {typed ?? template.DefaultName} was refused");
        return ItemTemplates.FilesFor(template, result.FileName!);
    }

    // ------------------------------------------------------------------
    // Every template writes something, under the names it promises, and
    // nothing it writes is blank when it is meant to hold code.
    // ------------------------------------------------------------------
    [Fact]
    public void EveryTemplateGeneratesItsFiles()
    {
        foreach (var template in ItemTemplates.All)
        {
            var files = Generate(template.Id);

            Assert.Equal(template.FileSuffixes.Count, files.Count);
            Assert.All(files, file => Assert.False(string.IsNullOrWhiteSpace(file.FileName), template.Id));

            // Only the empty ones are empty.
            var mayBeEmpty = template.Id is ItemTemplateIds.EmptyFile or ItemTemplateIds.TextFile or ItemTemplateIds.AnyFile;
            Assert.Equal(mayBeEmpty, files[0].Content.Length == 0);
        }
    }

    // ------------------------------------------------------------------
    // Visual Basic, compiled.
    // ------------------------------------------------------------------
    private static readonly MetadataReference[] PlatformReferences =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
    ];

    /// <summary>Compiles the sources as a project of this kind does: the SDK's implicit imports, no root namespace.</summary>
    private static IReadOnlyList<Diagnostic> Errors(IEnumerable<string> sources, params MetadataReference[] more)
    {
        var compilation = VisualBasicCompilation.Create(
            "AddItem",
            sources.Select(source => VisualBasicSyntaxTree.ParseText(source)),
            [.. PlatformReferences, .. more],
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(GlobalImport.Parse(
                    "Microsoft.VisualBasic", "System", "System.Collections.Generic", "System.Linq", "System.Threading.Tasks"))
                .WithRootNamespace(""));

        return [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static void AssertCompiles(string id, params MetadataReference[] more)
    {
        var files = Generate(id);
        var errors = Errors(files.Where(f => f.FileName.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)).Select(f => f.Content), more);
        Assert.True(errors.Count == 0, $"{id}:\n{string.Join("\n", errors)}\n\n{string.Join("\n----\n", files.Select(f => f.Content))}");
    }

    [Theory]
    [InlineData(ItemTemplateIds.Class)]
    [InlineData(ItemTemplateIds.Module)]
    [InlineData(ItemTemplateIds.Interface)]
    [InlineData(ItemTemplateIds.Structure)]
    [InlineData(ItemTemplateIds.Enum)]
    [InlineData(ItemTemplateIds.ExceptionClass)]
    [InlineData(ItemTemplateIds.EmptyFile)]
    [InlineData(ItemTemplateIds.Component)]
    public void TheCodeTemplatesCompile(string id) => AssertCompiles(id);

    [Theory]
    [InlineData(ItemTemplateIds.WindowsForm)]
    [InlineData(ItemTemplateIds.UserControl)]
    [InlineData(ItemTemplateIds.Dialog)]
    [InlineData(ItemTemplateIds.AboutBox)]
    public void TheWindowsFormsTemplatesCompileAgainstWindowsForms(string id) => AssertCompiles(id);

    [Theory]
    [InlineData(ItemTemplateIds.AvaloniaWindow)]
    [InlineData(ItemTemplateIds.AvaloniaUserControl)]
    public void TheAvaloniaTemplatesCompileAgainstAvalonia(string id) =>
        AssertCompiles(id,
            MetadataReference.CreateFromFile(typeof(Avalonia.Controls.Window).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.AvaloniaObject).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.Markup.Xaml.AvaloniaXamlLoader).Assembly.Location));

    [Theory]
    [InlineData(ItemTemplateIds.MvcController)]
    [InlineData(ItemTemplateIds.ApiController)]
    public void TheControllersCompileAgainstAspNetCore(string id) => AssertCompiles(id);

    [Fact]
    public void ControllersAreFoundByMvcAndApiControllersCarryTheirAttributes()
    {
        Assert.Contains("Inherits Controller", Generate(ItemTemplateIds.MvcController, "Orders")[0].Content);
        Assert.Contains("Public Function Index() As IActionResult", Generate(ItemTemplateIds.MvcController, "Orders")[0].Content);
        Assert.Equal("OrdersController.vb", Generate(ItemTemplateIds.MvcController, "Orders")[0].FileName);

        var api = Generate(ItemTemplateIds.ApiController, "Orders")[0].Content;
        Assert.Contains("<ApiController>", api);
        Assert.Contains("<Route(\"api/[controller]\")>", api);
        Assert.Contains("Inherits ControllerBase", api);
    }

    [Fact]
    public void AnExceptionHasTheThreeStandardConstructors()
    {
        var text = Generate(ItemTemplateIds.ExceptionClass, "InvoiceException")[0].Content;

        Assert.Contains("Public Sub New()", text);
        Assert.Contains("Public Sub New(message As String)", text);
        Assert.Contains("Public Sub New(message As String, innerException As Global.System.Exception)", text);
        Assert.Contains("Inherits Global.System.Exception", text);
    }

    [Fact]
    public void TheTestTemplatesCompileAgainstTheirFramework()
    {
        // xUnit is the one this test project is written with.
        AssertCompiles(ItemTemplateIds.XUnitTest,
            MetadataReference.CreateFromFile(typeof(Xunit.FactAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Xunit.Assert).Assembly.Location));

        AssertCompiles(ItemTemplateIds.NUnitTest, TestFramework("nunit", "nunit.framework.dll", NUnitStub));
        AssertCompiles(ItemTemplateIds.MSTestTest,
            TestFramework("mstest.testframework", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll", MSTestStub));
    }

    /// <summary>
    /// The framework's real assembly when the machine's NuGet cache has it,
    /// otherwise a stub of the few types the template uses: a clean machine
    /// cannot restore here, and a compile against nothing proves nothing.
    /// </summary>
    private static MetadataReference TestFramework(string package, string dll, string stub)
    {
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var folder = Path.Combine(cache, package);

        if (Directory.Exists(folder))
        {
            var real = Directory.EnumerateFiles(folder, dll, SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}net", StringComparison.Ordinal)
                            && !path.Contains("net4", StringComparison.Ordinal)
                            && !path.Contains("uap", StringComparison.Ordinal))
                .OrderDescending()
                .FirstOrDefault();

            if (real is not null) return MetadataReference.CreateFromFile(real);
        }

        return VisualBasicCompilation.Create("Stub" + package,
                [VisualBasicSyntaxTree.ParseText(stub)],
                PlatformReferences,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .ToMetadataReference();
    }

    private const string NUnitStub = """
        Namespace NUnit.Framework
            Public Class TestFixtureAttribute
                Inherits System.Attribute
            End Class
            Public Class TestAttribute
                Inherits System.Attribute
            End Class
            Public Class Assert
                Public Shared Sub Pass()
                End Sub
            End Class
        End Namespace
        """;

    private const string MSTestStub = """
        Namespace Microsoft.VisualStudio.TestTools.UnitTesting
            Public Class TestClassAttribute
                Inherits System.Attribute
            End Class
            Public Class TestMethodAttribute
                Inherits System.Attribute
            End Class
            Public Class Assert
                Public Shared Sub IsTrue(condition As Boolean)
                End Sub
            End Class
        End Namespace
        """;

    // ------------------------------------------------------------------
    // Windows Forms: the pair opens in the designer's document.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.WindowsForm, 0)]
    [InlineData(ItemTemplateIds.UserControl, 0)]
    [InlineData(ItemTemplateIds.Dialog, 2)]
    [InlineData(ItemTemplateIds.AboutBox, 5)]
    public void TheWindowsFormsPairsOpenInTheDesignerDocument(string id, int controls)
    {
        var files = Generate(id, "Sample");

        Assert.Equal(["Sample.vb", "Sample.Designer.vb"], files.Select(f => f.FileName));

        var document = new WinFormsDesignerDocument(files[1].Content);

        Assert.Equal("Sample", document.ClassName);
        Assert.Equal(controls, document.Controls.Count);
        Assert.Equal("\"Sample\"", document.Root.Properties["Name"]);
    }

    [Fact]
    public void ADialogIsAFixedFormWhoseButtonsAnswerEnterAndEscape()
    {
        var document = new WinFormsDesignerDocument(Generate(ItemTemplateIds.Dialog, "Ask")[1].Content);

        Assert.Equal("Me.OK_Button", document.Root.Properties["AcceptButton"]);
        Assert.Equal("Me.Cancel_Button", document.Root.Properties["CancelButton"]);
        Assert.Equal("Global.System.Windows.Forms.FormBorderStyle.FixedDialog", document.Root.Properties["FormBorderStyle"]);
        Assert.Equal(["OK_Button", "Cancel_Button"], document.Controls.Select(c => c.Name));
        Assert.Equal("Global.System.Windows.Forms.DialogResult.OK", document.Controls.Single(c => c.Name == "OK_Button").Properties["DialogResult"]);
        Assert.Equal("Global.System.Windows.Forms.DialogResult.Cancel", document.Controls.Single(c => c.Name == "Cancel_Button").Properties["DialogResult"]);
    }

    [Fact]
    public void ADesignerEditOfAGeneratedFormStillCompiles()
    {
        // The designer writes into these files: a control added to a generated
        // dialog must leave a file the compiler still accepts.
        var files = Generate(ItemTemplateIds.Dialog, "Ask");
        var edited = new WinFormsDesignerDocument(files[1].Content).AddControl("Label1", "Global.System.Windows.Forms.Label");

        Assert.Contains("Label1", edited);
        Assert.Empty(Errors([files[0].Content, edited]));
    }

    // ------------------------------------------------------------------
    // Avalonia: the XAML is the one Add Window writes, and loads.
    // ------------------------------------------------------------------
    [Fact]
    public void TheAvaloniaTemplatesWriteWhatAddWindowWrites()
    {
        var window = Generate(ItemTemplateIds.AvaloniaWindow, "Shell");

        Assert.Equal(["Shell.axaml", "Shell.axaml.vb"], window.Select(f => f.FileName));
        Assert.Equal(FormTemplates.WindowXaml("Shell", "Shell"), window[0].Content);
        Assert.Equal("Shell", XamlDocument.Parse(window[0].Content, "Shell.axaml").ClassName);
        Assert.Contains("Inherits Window", window[1].Content);

        var control = Generate(ItemTemplateIds.AvaloniaUserControl, "Meter");
        Assert.Equal(FormTemplates.UserControlXaml("Meter"), control[0].Content);
        Assert.Contains("Inherits UserControl", control[1].Content);
    }

    // ------------------------------------------------------------------
    // Razor and Blazor: parsed by the parser the compiler runs, and built
    // by the real generators in the folder each belongs in.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.MvcView)]
    [InlineData(ItemTemplateIds.LayoutPage)]
    [InlineData(ItemTemplateIds.PartialView)]
    [InlineData(ItemTemplateIds.BlazorComponent)]
    public void TheRazorTemplatesParseWithoutErrors(string id)
    {
        var file = Generate(id)[0];
        var document = VbHtmlParser.Parse(file.Content);

        Assert.True(document.Diagnostics.Count == 0,
            $"{id}: {string.Join("; ", document.Diagnostics.Select(d => $"{d.Id} {d.Message} at {d.Line}:{d.Column}"))}");
        Assert.NotEmpty(document.Nodes);
    }

    [Fact]
    public void AMvcViewDeclaresItsModelAndTheBlazorComponentItsParameter()
    {
        Assert.Equal("Object", VbHtmlParser.Parse(Generate(ItemTemplateIds.MvcView)[0].Content).ModelType);
        Assert.Contains("@RenderBody()", Generate(ItemTemplateIds.LayoutPage)[0].Content);
        Assert.Contains("@RenderSection(\"Scripts\", False)", Generate(ItemTemplateIds.LayoutPage)[0].Content);
        Assert.Contains("<Parameter>", Generate(ItemTemplateIds.BlazorComponent)[0].Content);
    }

    [Fact]
    public void AVbPageParsesWithoutErrors()
    {
        var page = VbPageParser.Parse(Generate(ItemTemplateIds.VbPage)[0].Content);

        Assert.Empty(page.Diagnostics);
        Assert.Contains(page.Parts, part => part is VbPageParser.Expression);
    }

    private static readonly string Site = Path.Combine(Path.GetTempPath(), "Site");

    [Theory]
    [InlineData(ItemTemplateIds.MvcView, "VbHtmlGenerator", "Views/Home")]
    [InlineData(ItemTemplateIds.LayoutPage, "VbHtmlGenerator", "Views/Shared")]
    [InlineData(ItemTemplateIds.PartialView, "VbHtmlGenerator", "Views/Shared")]
    [InlineData(ItemTemplateIds.BlazorComponent, "VbComponentGenerator", "Components")]
    [InlineData(ItemTemplateIds.VbPage, "VbPageGenerator", "Pages")]
    public void TheRazorTemplatesBuildThroughTheRealGenerators(string id, string generator, string folder)
    {
        var file = Generate(id)[0];
        var path = Path.Combine([Site, .. folder.Split('/'), file.FileName]);

        var outcome = GeneratorRun.Run(generator, Site, (path, file.Content));

        Assert.Null(outcome.Exception);
        Assert.DoesNotContain(outcome.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(outcome.CompilationErrors.Count == 0,
            $"{id}:\n{string.Join("\n", outcome.CompilationErrors)}\n{string.Join("\n", outcome.Sources.Values)}");
        Assert.NotEmpty(outcome.Sources);
    }

    // ------------------------------------------------------------------
    // Web and data files are what they claim to be.
    // ------------------------------------------------------------------
    [Fact]
    public void TheWebAndDataFilesAreWellFormed()
    {
        using (JsonDocument.Parse(Generate(ItemTemplateIds.JsonFile)[0].Content)) { }

        var html = XDocument.Parse(Generate(ItemTemplateIds.HtmlPage, "Home")[0].Content.Replace("<!DOCTYPE html>", ""));
        Assert.Equal("Home", html.Descendants("title").Single().Value);

        Assert.Contains("body {", Generate(ItemTemplateIds.StyleSheet)[0].Content);
        Assert.StartsWith("\"use strict\";", Generate(ItemTemplateIds.JavaScript)[0].Content);

        Assert.Equal("root", XDocument.Parse(Generate(ItemTemplateIds.XmlFile)[0].Content).Root!.Name.LocalName);

        var config = XDocument.Parse(Generate(ItemTemplateIds.AppConfig)[0].Content);
        Assert.Equal("configuration", config.Root!.Name.LocalName);
        Assert.NotNull(config.Root.Element("appSettings"));
    }

    [Fact]
    public void TheSchemaCompilesAndValidatesTheXmlFileTemplate()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add("", XmlReader.Create(new StringReader(Generate(ItemTemplateIds.XmlSchema)[0].Content)));
        schemas.Compile();

        var xml = XDocument.Parse("<root><item>one</item></root>");
        var problems = new List<string>();
        xml.Validate(schemas, (_, e) => problems.Add(e.Message));

        Assert.Empty(problems);
    }

    [Fact]
    public void TheResourcesFileIsReadByTheResourceReaderAndHasItsHeaders()
    {
        var text = Generate(ItemTemplateIds.ResourceFile)[0].Content;

        var headers = XDocument.Parse(text).Root!.Elements("resheader")
            .ToDictionary(e => (string)e.Attribute("name")!, e => e.Element("value")!.Value);
        Assert.Equal("text/microsoft-resx", headers["resmimetype"]);
        Assert.Equal("2.0", headers["version"]);
        Assert.Contains("ResXResourceReader", headers["reader"]);
        Assert.Contains("ResXResourceWriter", headers["writer"]);

        using var reader = System.Resources.ResXResourceReader.FromFileContents(text);
        Assert.Empty(reader.Cast<System.Collections.DictionaryEntry>());
    }
}
