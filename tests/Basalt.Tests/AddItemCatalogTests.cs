using System.Xml.Linq;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using static Basalt.Tests.WinFormsCompilationAssert;

namespace Basalt.Tests;

/// <summary>
/// The data-driven catalogue behind Add New Item: what it lists, how a typed
/// name becomes a file name, which templates a project fits, and how the search
/// narrows. The dialog, the preview and the build checks all read the same
/// records, so these are the rules they stand on.
/// </summary>
public sealed class AddItemCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("basalt-additem-catalog-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool Reserved(string name) => ExplorerFileOperations.IsVisualBasicReservedWord(name);

    private static ItemTemplateDefinition Template(string id) => ItemTemplates.ById(id);

    // ------------------------------------------------------------------
    // The catalogue is complete: every category has templates, every
    // template is named, described, iconed, and writes the files it says.
    // ------------------------------------------------------------------
    [Fact]
    public void EveryCategoryOffersTemplatesAndEveryTemplateIsComplete()
    {
        foreach (var category in ItemTemplateCatalog.Categories)
            Assert.NotEmpty(ItemTemplates.InCategory(category));

        Assert.Equal(ItemTemplates.All.Count, ItemTemplates.All.Select(t => t.Id).Distinct().Count());

        foreach (var template in ItemTemplates.All)
        {
            Assert.NotEqual(template.NameKey, template.Name);
            Assert.NotEqual(template.DescriptionKey, template.Description);
            Assert.True(template.Description.Length > 10, template.Id);

            Assert.True(Enum.TryParse<IconKind>(template.Icon, out var icon), $"{template.Id}: icon {template.Icon}");
            Assert.NotNull(IdeIcons.PathDataFor(icon));

            var result = ItemTemplates.Check(template, template.DefaultName, Reserved);
            Assert.True(result.IsValid, $"{template.Id}: the suggested name {template.DefaultName} is refused");
            Assert.Equal(template.DefaultName, result.FileName);

            var files = ItemTemplates.FilesFor(template, result.FileName!);
            Assert.Equal(template.FileSuffixes.Count, files.Count);

            var typeName = template.Extension.Length > 0
                ? result.FileName![..^template.Extension.Length]
                : result.FileName!;

            for (var i = 0; i < files.Count; i++)
                Assert.Equal(typeName + template.FileSuffixes[i], files[i].FileName);
        }
    }

    [Fact]
    public void EveryCategoryHasAName()
    {
        foreach (var category in ItemTemplateCatalog.Categories)
        {
            var key = ItemTemplates.CategoryKey(category);
            Assert.NotEqual(key, Localizer.Get(key));
        }
    }

    // ------------------------------------------------------------------
    // The templates of the brief are all there, in the categories asked.
    // ------------------------------------------------------------------
    [Fact]
    public void TheCatalogueHasTheTemplatesAskedFor()
    {
        static string[] Names(ItemCategory category) => [.. ItemTemplates.InCategory(category).Select(t => t.Id)];

        Assert.Equal(
            [ItemTemplateIds.Class, ItemTemplateIds.Module, ItemTemplateIds.Interface, ItemTemplateIds.Structure,
             ItemTemplateIds.Enum, ItemTemplateIds.ExceptionClass, ItemTemplateIds.EmptyFile],
            Names(ItemCategory.Code));
        Assert.Equal(
            [ItemTemplateIds.WindowsForm, ItemTemplateIds.UserControl, ItemTemplateIds.Dialog,
             ItemTemplateIds.AboutBox, ItemTemplateIds.Component],
            Names(ItemCategory.WindowsForms));
        Assert.Equal([ItemTemplateIds.AvaloniaWindow, ItemTemplateIds.AvaloniaUserControl], Names(ItemCategory.Avalonia));
        Assert.Equal(
            [ItemTemplateIds.HtmlPage, ItemTemplateIds.StyleSheet, ItemTemplateIds.JavaScript, ItemTemplateIds.JsonFile],
            Names(ItemCategory.Web));
        Assert.Equal(
            [ItemTemplateIds.MvcView, ItemTemplateIds.LayoutPage, ItemTemplateIds.PartialView,
             ItemTemplateIds.MvcController, ItemTemplateIds.ApiController, ItemTemplateIds.BlazorComponent,
             ItemTemplateIds.VbPage],
            Names(ItemCategory.Razor));
        Assert.Equal(
            [ItemTemplateIds.XmlFile, ItemTemplateIds.XmlSchema, ItemTemplateIds.TextFile,
             ItemTemplateIds.ResourceFile, ItemTemplateIds.AppConfig],
            Names(ItemCategory.Data));
        Assert.Equal(
            [ItemTemplateIds.XUnitTest, ItemTemplateIds.NUnitTest, ItemTemplateIds.MSTestTest],
            Names(ItemCategory.Testing));
        Assert.NotEmpty(Names(ItemCategory.General));
    }

    // ------------------------------------------------------------------
    // The six templates that existed before the catalogue write what they
    // always wrote.
    // ------------------------------------------------------------------
    [Fact]
    public void TheOriginalSixWriteWhatTheyAlwaysWrote()
    {
        Assert.Equal("Public Class Customer\nEnd Class\n".ReplaceLineEndings("\n"),
            Text(ItemTemplateIds.Class, "Customer.vb"));
        Assert.Equal("Module Helpers\nEnd Module\n", Text(ItemTemplateIds.Module, "Helpers.vb"));
        Assert.Equal("Public Interface IStore\nEnd Interface\n", Text(ItemTemplateIds.Interface, "IStore.vb"));
        Assert.Equal("", Text(ItemTemplateIds.EmptyFile, "Scratch.vb"));

        var form = ItemTemplates.FilesFor(Template(ItemTemplateIds.WindowsForm), "Form1.vb");
        Assert.Equal(["Form1.vb", "Form1.Designer.vb"], form.Select(f => f.FileName));
        Assert.Equal("Public Partial Class Form1\n    Public Sub New()\n        InitializeComponent()\n    End Sub\nEnd Class\n",
            form[0].Content.ReplaceLineEndings("\n"));

        // The designer file, character for character as the WinForms project
        // template's MainForm writes it, with the name swapped.
        Assert.Equal("""
            <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated>
            Public Partial Class Form1
                Inherits Global.System.Windows.Forms.Form

                Private components As Global.System.ComponentModel.IContainer

                Protected Overrides Sub Dispose(disposing As Boolean)
                    Try
                        If disposing AndAlso components IsNot Nothing Then
                            components.Dispose()
                        End If
                    Finally
                        MyBase.Dispose(disposing)
                    End Try
                End Sub

                <Global.System.Diagnostics.DebuggerStepThrough>
                Private Sub InitializeComponent()
                    Me.components = New Global.System.ComponentModel.Container()
                    Me.SuspendLayout()
                    Me.AutoScaleDimensions = New Global.System.Drawing.SizeF(96.0F, 96.0F)
                    Me.AutoScaleMode = Global.System.Windows.Forms.AutoScaleMode.Dpi
                    Me.ClientSize = New Global.System.Drawing.Size(800, 500)
                    Me.Name = "Form1"
                    Me.Text = "Form1"
                    Me.ResumeLayout(False)
                End Sub
            End Class

            """.ReplaceLineEndings("\n"), form[1].Content.ReplaceLineEndings("\n"));

        var control = ItemTemplates.FilesFor(Template(ItemTemplateIds.UserControl), "Meter.vb");
        Assert.Contains("Inherits Global.System.Windows.Forms.UserControl", control[1].Content);
        Assert.Contains("Me.Size = New Global.System.Drawing.Size(150, 150)", control[1].Content);
        Assert.DoesNotContain("Me.Text", control[1].Content);
    }

    private static string Text(string id, string fileName) =>
        ItemTemplates.FilesFor(Template(id), fileName)[0].Content.ReplaceLineEndings("\n");

    // ------------------------------------------------------------------
    // Names: the extension is added, a typed one is not doubled, a code item
    // needs an identifier that is not a keyword, a file item only a file name.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.Class, "Customer", "Customer.vb")]
    [InlineData(ItemTemplateIds.Class, "Customer.vb", "Customer.vb")]
    [InlineData(ItemTemplateIds.Class, "  Customer.VB ", "Customer.vb")]
    [InlineData(ItemTemplateIds.MvcView, "Index", "Index.vbhtml")]
    [InlineData(ItemTemplateIds.MvcView, "Index.vbhtml", "Index.vbhtml")]
    [InlineData(ItemTemplateIds.MvcView, "_Layout", "_Layout.vbhtml")]
    [InlineData(ItemTemplateIds.BlazorComponent, "Counter", "Counter.vbrazor")]
    [InlineData(ItemTemplateIds.VbPage, "About", "About.vbpage")]
    [InlineData(ItemTemplateIds.HtmlPage, "index", "index.html")]
    [InlineData(ItemTemplateIds.HtmlPage, "index.htm", "index.htm")]
    [InlineData(ItemTemplateIds.StyleSheet, "site.scss", "site.scss")]
    [InlineData(ItemTemplateIds.AppConfig, "App", "App.config")]
    [InlineData(ItemTemplateIds.AppConfig, "Web.config", "Web.config")]
    [InlineData(ItemTemplateIds.AnyFile, ".gitignore", ".gitignore")]
    [InlineData(ItemTemplateIds.AnyFile, "Makefile", "Makefile")]
    [InlineData(ItemTemplateIds.EmptyFile, "Scratch", "Scratch.vb")]
    [InlineData(ItemTemplateIds.EmptyFile, "notes.txt", "notes.txt")]
    [InlineData(ItemTemplateIds.AvaloniaWindow, "Main", "Main.axaml")]
    // A controller must end in "Controller" for MVC to find it.
    [InlineData(ItemTemplateIds.MvcController, "Products", "ProductsController.vb")]
    [InlineData(ItemTemplateIds.MvcController, "ProductsController", "ProductsController.vb")]
    [InlineData(ItemTemplateIds.ApiController, "Orders.vb", "OrdersController.vb")]
    public void ATypedNameBecomesTheFileNameWithItsExtension(string id, string typed, string expected)
    {
        var result = ItemTemplates.Check(Template(id), typed, Reserved);

        Assert.True(result.IsValid, $"{typed} was refused: {result.Problem}");
        Assert.Equal(expected, result.FileName);
    }

    [Theory]
    [InlineData(ItemTemplateIds.Class, "", ItemNameProblem.Empty)]
    [InlineData(ItemTemplateIds.Class, "   ", ItemNameProblem.Empty)]
    [InlineData(ItemTemplateIds.Class, ".vb", ItemNameProblem.Empty)]
    [InlineData(ItemTemplateIds.Class, "9Lives", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.Class, "My Class", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.Class, "A-B", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.Class, "End", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.Module, "Sub", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.WindowsForm, "Form.1", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.BlazorComponent, "Bad Name", ItemNameProblem.NotAnIdentifier)]
    [InlineData(ItemTemplateIds.MvcView, "", ItemNameProblem.Empty)]
    [InlineData(ItemTemplateIds.MvcView, ".vbhtml", ItemNameProblem.Empty)]
    [InlineData(ItemTemplateIds.MvcView, "a/b", ItemNameProblem.InvalidFileName)]
    [InlineData(ItemTemplateIds.MvcView, "..", ItemNameProblem.InvalidFileName)]
    public void ANameThatCannotBeOneIsRefusedWithTheReason(string id, string typed, ItemNameProblem expected)
    {
        var result = ItemTemplates.Check(Template(id), typed, Reserved);

        Assert.Equal(expected, result.Problem);
        Assert.Null(result.FileName);
    }

    // A view is named like a file, so a keyword is fine there: "End.vbhtml" is a view.
    [Fact]
    public void AViewMayBeNamedAfterAKeywordButAClassMayNot()
    {
        Assert.True(ItemTemplates.Check(Template(ItemTemplateIds.MvcView), "End", Reserved).IsValid);
        Assert.False(ItemTemplates.Check(Template(ItemTemplateIds.Class), "End", Reserved).IsValid);
    }

    // ------------------------------------------------------------------
    // Suggested names count up, as Visual Studio's Class1, Class2 do.
    // ------------------------------------------------------------------
    [Fact]
    public void ASuggestedNameCountsUpPastWhatTheFolderHolds()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string name) => existing.Contains(name);

        var template = Template(ItemTemplateIds.Class);
        Assert.Equal("Class1.vb", ItemTemplates.SuggestName(template, Exists));

        existing.Add("Class1.vb");
        Assert.Equal("Class2.vb", ItemTemplates.SuggestName(template, Exists));

        existing.Add("Class2.vb");
        Assert.Equal("Class3.vb", ItemTemplates.SuggestName(template, Exists));

        // A pair counts as taken when either of its files is.
        var form = Template(ItemTemplateIds.WindowsForm);
        existing.Add("Form1.Designer.vb");
        Assert.Equal("Form2.vb", ItemTemplates.SuggestName(form, Exists));

        // A name with no number has used up its first: the next is 2.
        existing.Add("App.config");
        Assert.Equal("App2.config", ItemTemplates.SuggestName(Template(ItemTemplateIds.AppConfig), Exists));
    }

    // ------------------------------------------------------------------
    // A controller must end in "Controller", so counting up happens before
    // that suffix: HomeController is followed by Home2Controller, and the file
    // looked for is the one that would be written (not HomeController1.vb,
    // which Add would turn into HomeController1Controller.vb).
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.MvcController, "HomeController.vb", "Home2Controller.vb")]
    [InlineData(ItemTemplateIds.ApiController, "ValuesController.vb", "Values2Controller.vb")]
    public void ASuggestedControllerNameCountsUpBeforeItsSuffix(string id, string existingFile, string expected)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { existingFile };
        var template = Template(id);

        var suggested = ItemTemplates.SuggestName(template, existing.Contains);

        Assert.Equal(expected, suggested);

        // The suggestion is a name Add accepts as it stands, and writes under that name.
        var result = ItemTemplates.Check(template, suggested, Reserved);
        Assert.Equal(expected, result.FileName);
        Assert.Equal(expected, ItemTemplates.FilesFor(template, result.FileName!)[0].FileName);

        existing.Add(expected);
        Assert.Equal(expected.Replace("2", "3"), ItemTemplates.SuggestName(template, existing.Contains));
    }

    // ------------------------------------------------------------------
    // A dot in the name of a file item is part of the name unless what
    // follows it is an extension that kind of file has.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.EmptyFile, "Models.Customer", "Models.Customer.vb")]
    [InlineData(ItemTemplateIds.EmptyFile, "Models.Customer.vb", "Models.Customer.vb")]
    [InlineData(ItemTemplateIds.EmptyFile, "Foo.", "Foo.vb")]
    [InlineData(ItemTemplateIds.EmptyFile, "notes.txt", "notes.txt")]
    [InlineData(ItemTemplateIds.JavaScript, "app.min", "app.min.js")]
    [InlineData(ItemTemplateIds.JavaScript, "app.mjs", "app.mjs")]
    [InlineData(ItemTemplateIds.HtmlPage, "page.v2", "page.v2.html")]
    [InlineData(ItemTemplateIds.HtmlPage, "page.htm", "page.htm")]
    [InlineData(ItemTemplateIds.StyleSheet, "site.theme", "site.theme.css")]
    [InlineData(ItemTemplateIds.AnyFile, "Foo.", "Foo")]
    public void ADottedNameKeepsItsDotsAndGetsTheExtensionOfItsKind(string id, string typed, string expected)
    {
        var result = ItemTemplates.Check(Template(id), typed, Reserved);

        Assert.True(result.IsValid, $"{typed}: {result.Problem}");
        Assert.Equal(expected, result.FileName);
        Assert.DoesNotContain("..", result.FileName);

        // And the file written is the one named.
        Assert.Equal(expected, ItemTemplates.FilesFor(Template(id), result.FileName!)[0].FileName);
    }

    // ------------------------------------------------------------------
    // An item cannot be named after the class it inherits from.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ItemTemplateIds.MvcController, "Controller")]
    [InlineData(ItemTemplateIds.ApiController, "Controller")]
    [InlineData(ItemTemplateIds.AvaloniaWindow, "Window")]
    [InlineData(ItemTemplateIds.AvaloniaUserControl, "UserControl")]
    [InlineData(ItemTemplateIds.WindowsForm, "Form")]
    [InlineData(ItemTemplateIds.Dialog, "form")]
    [InlineData(ItemTemplateIds.UserControl, "UserControl")]
    [InlineData(ItemTemplateIds.Component, "Component")]
    public void AnItemNamedAfterItsOwnBaseClassIsRefused(string id, string typed)
    {
        var result = ItemTemplates.Check(Template(id), typed, Reserved);

        Assert.Equal(ItemNameProblem.ShadowsBaseType, result.Problem);
        Assert.Null(result.FileName);

        // With the extension typed too.
        Assert.Equal(ItemNameProblem.ShadowsBaseType,
            ItemTemplates.Check(Template(id), typed + Template(id).Extension, Reserved).Problem);
    }

    [Fact]
    public void ANameThatOnlyStartsWithTheBaseClassIsFine()
    {
        Assert.True(ItemTemplates.Check(Template(ItemTemplateIds.AvaloniaWindow), "Window1", Reserved).IsValid);
        Assert.True(ItemTemplates.Check(Template(ItemTemplateIds.MvcController), "Home", Reserved).IsValid);
        Assert.True(ItemTemplates.Check(Template(ItemTemplateIds.WindowsForm), "MainForm", Reserved).IsValid);
    }

    // ------------------------------------------------------------------
    // Search: every word must be in the name or the description.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData("blazor", true)]
    [InlineData("BLAZOR", true)]
    [InlineData("component blazor", true)]
    [InlineData("constructors", false)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("windows", false)]
    public void SearchMatchesNameAndDescription(string query, bool blazorComponentMatches)
    {
        Assert.Equal(blazorComponentMatches, ItemTemplates.Matches(Template(ItemTemplateIds.BlazorComponent), query));
    }

    [Fact]
    public void SearchFindsATemplateByItsDescriptionAlone()
    {
        var exception = Template(ItemTemplateIds.ExceptionClass);

        Assert.DoesNotContain("constructors", exception.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(ItemTemplates.Matches(exception, "constructors"));
        Assert.Equal([ItemTemplateIds.ExceptionClass],
            ItemTemplates.All.Where(t => ItemTemplates.Matches(t, "constructors")).Select(t => t.Id));
    }

    // ------------------------------------------------------------------
    // Which project a template fits.
    // ------------------------------------------------------------------
    private static ItemProjectProfile Profile(string projectXml) => ItemProjectProfile.FromProjectXml(XDocument.Parse(projectXml));

    [Fact]
    public void AWebProjectIsRecognisedFromItsSdkItsFrameworkOrItsPackages()
    {
        Assert.True(Profile("<Project Sdk=\"Microsoft.NET.Sdk.Web\"/>").IsWeb);
        Assert.True(Profile("<Project Sdk=\"Microsoft.NET.Sdk.Razor\"/>").IsWeb);
        Assert.True(Profile("<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\"/></ItemGroup></Project>").IsWeb);
        Assert.True(Profile("<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Basalt.Razor.Vb.AspNetCore\" Version=\"1.0.0\"/></ItemGroup></Project>").IsWeb);

        Assert.False(Profile("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>").IsWeb);
    }

    [Fact]
    public void AWindowsFormsProjectIsRecognisedTheWayTheWinFormsTemplateSaysSo()
    {
        Assert.True(Profile("<Project><PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup></Project>").IsWindowsForms);
        Assert.True(Profile("<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>").IsWindowsForms);
        Assert.True(Profile("<Project><ItemGroup><Import Include=\"System.Windows.Forms\"/></ItemGroup></Project>").IsWindowsForms);
        Assert.False(Profile("<Project><PropertyGroup><UseWindowsForms>false</UseWindowsForms></PropertyGroup></Project>").IsWindowsForms);
    }

    [Fact]
    public void ATestProjectIsReadForTheFrameworkItReferences()
    {
        var xunit = Profile("<Project><ItemGroup><PackageReference Include=\"xunit.v3\" Version=\"3\"/><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17\"/></ItemGroup></Project>");
        Assert.True(xunit.HasXUnit);
        Assert.False(xunit.HasNUnit);
        Assert.False(xunit.HasMSTest);

        var nunit = Profile("<Project><ItemGroup><PackageReference Include=\"NUnit\" Version=\"4\"/><PackageReference Include=\"NUnit3TestAdapter\" Version=\"4\"/></ItemGroup></Project>");
        Assert.True(nunit.HasNUnit);
        Assert.False(nunit.HasXUnit);

        // A runner, an adapter or an analyzer alone runs or checks tests; it does not write them.
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"NUnit3TestAdapter\" Version=\"4\"/></ItemGroup></Project>").HasNUnit);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"NUnit.Analyzers\" Version=\"4\"/></ItemGroup></Project>").HasNUnit);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3\"/></ItemGroup></Project>").HasXUnit);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"xunit.v3.runner.inproc.console\" Version=\"3\"/></ItemGroup></Project>").HasXUnit);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"xunit.analyzers\" Version=\"1\"/></ItemGroup></Project>").HasXUnit);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"MSTest.TestAdapter\" Version=\"3\"/></ItemGroup></Project>").HasMSTest);
        Assert.False(Profile("<Project><ItemGroup><PackageReference Include=\"MSTest.Analyzers\" Version=\"3\"/></ItemGroup></Project>").HasMSTest);
        Assert.True(Profile("<Project><ItemGroup><PackageReference Include=\"xunit\" Version=\"2\"/></ItemGroup></Project>").HasXUnit);

        Assert.True(Profile("<Project><ItemGroup><PackageReference Include=\"MSTest.TestFramework\" Version=\"3\"/></ItemGroup></Project>").HasMSTest);
        Assert.True(Profile("<Project Sdk=\"MSTest.Sdk/3.9.3\"/>").HasMSTest);
        Assert.True(Profile("<Project><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"12\"/></ItemGroup></Project>").IsAvalonia);
    }

    [Fact]
    public void AProjectThatCannotBeReadFitsOnlyThePlainItems()
    {
        var broken = Path.Combine(_folder, "Broken.vbproj");
        File.WriteAllText(broken, "<Project><unclosed>");

        foreach (var profile in new[]
        {
            ItemProjectProfile.FromProjectFile(broken),
            ItemProjectProfile.FromProjectFile(Path.Combine(_folder, "Missing.vbproj")),
            ItemProjectProfile.FromProjectFile(null)
        })
        {
            Assert.Null(ItemTemplates.Misfit(Template(ItemTemplateIds.Class), profile));
            Assert.Equal(ItemRequirement.Web, ItemTemplates.Misfit(Template(ItemTemplateIds.MvcView), profile));
            Assert.Equal(ItemRequirement.WindowsForms, ItemTemplates.Misfit(Template(ItemTemplateIds.WindowsForm), profile));
        }
    }

    [Fact]
    public void ATemplateIsMarkedOnlyWhereTheProjectLacksWhatItNeeds()
    {
        var web = Profile("<Project Sdk=\"Microsoft.NET.Sdk.Web\"/>");
        var xunit = Profile("<Project><ItemGroup><PackageReference Include=\"xunit\" Version=\"2\"/></ItemGroup></Project>");

        Assert.Null(ItemTemplates.Misfit(Template(ItemTemplateIds.MvcView), web));
        Assert.Null(ItemTemplates.Misfit(Template(ItemTemplateIds.BlazorComponent), web));
        Assert.Equal(ItemRequirement.WindowsForms, ItemTemplates.Misfit(Template(ItemTemplateIds.Dialog), web));

        Assert.Null(ItemTemplates.Misfit(Template(ItemTemplateIds.XUnitTest), xunit));
        Assert.Equal(ItemRequirement.NUnit, ItemTemplates.Misfit(Template(ItemTemplateIds.NUnitTest), xunit));
        Assert.Equal(ItemRequirement.MSTest, ItemTemplates.Misfit(Template(ItemTemplateIds.MSTestTest), xunit));
    }

    // ------------------------------------------------------------------
    // The category memory.
    // ------------------------------------------------------------------
    [Fact]
    public void TheLastCategoryIsKeptBesideTheSettingsAndForgottenGracefully()
    {
        var settings = Path.Combine(_folder, "settings.json");
        var path = Basalt.Shell.AddItemMemory.PathBeside(settings);
        Assert.Equal(Path.Combine(_folder, "add-item.json"), path);
        Assert.Equal(Path.Combine(_folder, "mine.add-item.json"),
            Basalt.Shell.AddItemMemory.PathBeside(Path.Combine(_folder, "mine.json")));

        Assert.Equal(ItemCategory.Code, Basalt.Shell.AddItemMemory.Load(path));

        Basalt.Shell.AddItemMemory.Save(path, ItemCategory.Razor);
        Assert.Equal(ItemCategory.Razor, Basalt.Shell.AddItemMemory.Load(path));

        File.WriteAllText(path, "{ not json");
        Assert.Equal(ItemCategory.Code, Basalt.Shell.AddItemMemory.Load(path));

        File.WriteAllText(path, "{\"Category\":\"Nowhere\"}");
        Assert.Equal(ItemCategory.Code, Basalt.Shell.AddItemMemory.Load(path));
    }
}
