using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The plain-filesystem half of Visual Studio parity for "Project resource file": finding the
/// .vbproj a form belongs to, listing My Project\Resources.resx, and importing a new file the
/// way VbMyResourcesResXFileCodeGenerator would. No Avalonia: <see cref="WinFormsImageEditorUiTests"/>
/// proves the dialog and the designer actually agree with this.
/// </summary>
public sealed class WinFormsProjectResourcesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-project-resources-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string WriteProject(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Vbproj(string rootNamespace = "App") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>{rootNamespace}</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    private static byte[] TinyPng { get; } = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private string WritePng(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, TinyPng);
        return path;
    }

    // ---- FindProject -----------------------------------------------------

    [Fact]
    public void FindProjectWalksUpFromTheFormToItsVbproj()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var formPath = Path.Combine(_root, "App", "gui", "Form1.Designer.vb");
        Directory.CreateDirectory(Path.GetDirectoryName(formPath)!);
        File.WriteAllText(formPath, "Partial Class Form1\nEnd Class\n");

        Assert.Equal(project, WinFormsProjectResources.FindProject(formPath));
    }

    [Fact]
    public void FindProjectIsNullWhenTheNearestProjectIsACsproj()
    {
        WriteProject("App/App.csproj", "<Project />");
        var formPath = Path.Combine(_root, "App", "Form1.Designer.vb");
        File.WriteAllText(formPath, "");

        Assert.Null(WinFormsProjectResources.FindProject(formPath));
    }

    [Fact]
    public void FindProjectIsNullForABareFileNameWithNoRealFolder()
    {
        // What the headless designer tests pass for their in-memory documents: this must not
        // throw trying to enumerate an empty directory.
        Assert.Null(WinFormsProjectResources.FindProject("MainForm.Designer.vb"));
    }

    // ---- RootNamespace -----------------------------------------------------

    [Fact]
    public void RootNamespaceReadsTheProjectElementWhenPresent()
    {
        var project = WriteProject("App/App.vbproj", Vbproj("Contoso.App"));
        Assert.Equal("Contoso.App", WinFormsProjectResources.RootNamespace(project));
    }

    [Fact]
    public void RootNamespaceFallsBackToTheProjectFileNameWhenAbsent()
    {
        var project = WriteProject("App/App.vbproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Assert.Equal("App", WinFormsProjectResources.RootNamespace(project));
    }

    // ---- ListEntries -----------------------------------------------------

    [Fact]
    public void ListEntriesIsEmptyWithoutAResourcesResx()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        Assert.Empty(WinFormsProjectResources.ListEntries(project));
    }

    [Fact]
    public void ListEntriesResolvesAResXFileRefRelativeToMyProject()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var imageDirectory = Path.Combine(_root, "App", "Resources");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllBytes(Path.Combine(imageDirectory, "Logo.png"), TinyPng);
        WriteProject("App/My Project/Resources.resx", WinFormsResourceDocumentTemplate.Empty.Replace(
            "</root>",
            "  <data name=\"Logo\" type=\"System.Resources.ResXFileRef, System.Windows.Forms\">" +
            "<value>..\\Resources\\Logo.png;System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, " +
            "Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a</value></data>\n</root>"));

        var entries = WinFormsProjectResources.ListEntries(project);
        var entry = Assert.Single(entries);
        Assert.Equal("Logo", entry.Name);
        Assert.False(entry.IsIcon);
        Assert.Equal(TinyPng, entry.Bytes);
    }

    [Fact]
    public void ListEntriesDecodesAnEmbeddedByteArrayEntryTooNotOnlyResXFileRef()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var (value, typeName, mimeType) = WinFormsImageResources.Encode(TinyPng, isIcon: true);
        var resources = new WinFormsResourceDocument(WinFormsResourceDocumentTemplate.Empty)
            .SetValue("EmbeddedIcon", value, typeName, mimeType);
        WriteProject("App/My Project/Resources.resx", resources);

        var entry = Assert.Single(WinFormsProjectResources.ListEntries(project));
        Assert.Equal("EmbeddedIcon", entry.Name);
        Assert.True(entry.IsIcon);
        Assert.Equal(TinyPng, entry.Bytes);
    }

    // ---- Import -----------------------------------------------------

    [Fact]
    public void ImportCreatesResourcesResxDesignerVbAndTheProjectItemsWhenNoneExistYet()
    {
        var project = WriteProject("App/App.vbproj", Vbproj("Contoso.App"));
        var png = WritePng("logo.png");

        var entry = WinFormsProjectResources.Import(project, png, isIcon: false);

        Assert.Equal("logo", entry.Name);
        Assert.False(entry.IsIcon);
        Assert.Equal(TinyPng, entry.Bytes);

        var resxPath = WinFormsProjectResources.ResourcesResxPath(project);
        var designerPath = WinFormsProjectResources.ResourcesDesignerVbPath(project);
        Assert.True(File.Exists(resxPath));
        Assert.True(File.Exists(designerPath));

        var resxText = File.ReadAllText(resxPath);
        Assert.Contains(
            "<data name=\"logo\" type=\"System.Resources.ResXFileRef, System.Windows.Forms\">" +
            "<value>..\\Resources\\logo.png;System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, " +
            "Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a</value></data>", resxText, StringComparison.Ordinal);
        Assert.DoesNotContain("xml:space", resxText.Substring(resxText.IndexOf("name=\"logo\"", StringComparison.Ordinal)));

        var designerText = File.ReadAllText(designerPath);
        Assert.Contains("Namespace My.Resources", designerText, StringComparison.Ordinal);
        Assert.Contains("Friend Module Resources", designerText, StringComparison.Ordinal);
        Assert.Contains(
            "New Global.System.Resources.ResourceManager(\"Contoso.App.Resources\", GetType(Resources).Assembly)",
            designerText, StringComparison.Ordinal);
        Assert.Contains("Friend ReadOnly Property logo() As System.Drawing.Bitmap", designerText, StringComparison.Ordinal);
        Assert.Contains(
            "Return CType(ResourceManager.GetObject(\"logo\", resourceCulture), System.Drawing.Bitmap)",
            designerText, StringComparison.Ordinal);

        var copied = Path.Combine(WinFormsProjectResources.ImportedFilesDirectory(project), "logo.png");
        Assert.True(File.Exists(copied));
        Assert.Equal(TinyPng, File.ReadAllBytes(copied));

        var projectText = File.ReadAllText(project);
        Assert.Contains("VbMyResourcesResXFileCodeGenerator", projectText, StringComparison.Ordinal);
        Assert.Contains("CustomToolNamespace>My.Resources<", projectText, StringComparison.Ordinal);
        Assert.Contains("DependentUpon>Resources.resx<", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportOfAnIconWritesTheIconType()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var icon = Path.Combine(_root, "spinner.ico");
        File.WriteAllBytes(icon, TinyPng);

        var entry = WinFormsProjectResources.Import(project, icon, isIcon: true);

        Assert.True(entry.IsIcon);
        var designerText = File.ReadAllText(WinFormsProjectResources.ResourcesDesignerVbPath(project));
        Assert.Contains("Friend ReadOnly Property spinner() As System.Drawing.Icon", designerText, StringComparison.Ordinal);
        var resxText = File.ReadAllText(WinFormsProjectResources.ResourcesResxPath(project));
        Assert.Contains("System.Drawing.Icon, System.Drawing, Version=4.0.0.0", resxText, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportingTwoFilesWithTheSameNameGetsAUniqueSecondNameAndKeepsTheFirstEntryIntact()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var first = WritePng("icon.png");
        var secondSource = Path.Combine(_root, "nested", "icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(secondSource)!);
        File.WriteAllBytes(secondSource, TinyPng);

        var firstEntry = WinFormsProjectResources.Import(project, first, isIcon: false);
        var resxAfterFirst = File.ReadAllText(WinFormsProjectResources.ResourcesResxPath(project));

        var secondEntry = WinFormsProjectResources.Import(project, secondSource, isIcon: false);

        Assert.Equal("icon", firstEntry.Name);
        Assert.NotEqual(firstEntry.Name, secondEntry.Name);
        Assert.StartsWith("icon", secondEntry.Name, StringComparison.Ordinal);

        var resxAfterSecond = File.ReadAllText(WinFormsProjectResources.ResourcesResxPath(project));
        Assert.Contains(resxAfterFirst.Replace("</root>", ""), resxAfterSecond, StringComparison.Ordinal);

        var entries = WinFormsProjectResources.ListEntries(project);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ImportTwiceDoesNotDuplicateTheProjectItemGroup()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        WinFormsProjectResources.Import(project, WritePng("a.png"), isIcon: false);
        WinFormsProjectResources.Import(project, WritePng("b.png"), isIcon: false);

        var projectText = File.ReadAllText(project);
        var occurrences = projectText.Split("VbMyResourcesResXFileCodeGenerator").Length - 1;
        Assert.Equal(1, occurrences);
    }

    // ---- RootNamespace: explicit empty vs. absent -----------------------------------------

    [Fact]
    public void RootNamespaceIsEmptyWhenTheElementIsPresentButExplicitlyEmpty()
    {
        // <RootNamespace></RootNamespace> is a deliberate "no root namespace" in Visual
        // Basic (see VbProjectFile.SetPropertyAllowingEmpty), not the same as the element
        // being absent, which falls back to the project's file name instead.
        var project = WriteProject("App/App.vbproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        Assert.Equal("", WinFormsProjectResources.RootNamespace(project));
    }

    // ---- Import: name collisions with VB reserved words and generated members -------------

    [Fact]
    public void ImportEscapesAFileNameThatIsAVbReservedWord()
    {
        var project = WriteProject("App/App.vbproj", Vbproj());
        var png = WritePng("End.png");

        var entry = WinFormsProjectResources.Import(project, png, isIcon: false);

        // "End" does not compile as "Friend ReadOnly Property End() As ...": Visual Studio's
        // StronglyTypedResourceBuilder prefixes an underscore for exactly this reason.
        Assert.Equal("_End", entry.Name);
        var designerText = File.ReadAllText(WinFormsProjectResources.ResourcesDesignerVbPath(project));
        Assert.Contains("Friend ReadOnly Property _End() As System.Drawing.Bitmap", designerText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ResourceManager")]
    [InlineData("Culture")]
    public void ImportEscapesAFileNameThatCollidesWithAnAlreadyGeneratedMember(string reservedMemberName)
    {
        // ResourceManager and Culture are never resx entries, so they are not already in
        // existingNames the way a second "logo" import would be; they still have to be
        // avoided, or the generated module would declare the property twice.
        var project = WriteProject("App/App.vbproj", Vbproj());
        var png = WritePng(reservedMemberName + ".png");

        var entry = WinFormsProjectResources.Import(project, png, isIcon: false);

        Assert.Equal("_" + reservedMemberName, entry.Name);
    }

    // ---- Import: tolerant of real Visual Studio formatting --------------------------------

    /// <summary>
    /// A real Visual Studio-authored Resources.Designer.vb and Resources.resx, reformatted
    /// with CRLF line endings and tabs instead of VS's own four spaces: the full generated
    /// header comment, attributes and ResourceManager/Culture members are all here, the same
    /// shape as <c>Fixtures/VisualStudio/formatting-utility/vb/Resources.Designer.vb</c>, not
    /// a trimmed-down stand-in for it.
    /// </summary>
    private static string TabIndentedDesignerVb(string rootNamespace) => string.Join("\r\n",
    [
        "'------------------------------------------------------------------------------",
        "' <auto-generated>",
        "'     This code was generated by a tool.",
        "'     Runtime Version:4.0.30319.1",
        "'",
        "'     Changes to this file may cause incorrect behavior and will be lost if",
        "'     the code is regenerated.",
        "' </auto-generated>",
        "'------------------------------------------------------------------------------",
        "",
        "Option Strict On",
        "Option Explicit On",
        "",
        "Imports System",
        "",
        "Namespace My.Resources",
        "",
        "\t'This class was auto-generated by the StronglyTypedResourceBuilder",
        "\t'class via a tool like ResGen or Visual Studio.",
        "\t'To add or remove a member, edit your .ResX file then rerun ResGen",
        "\t'with the /str option, or rebuild your VS project.",
        "\t'''<summary>",
        "\t'''  A strongly-typed resource class, for looking up localized strings, etc.",
        "\t'''</summary>",
        "\t<Global.System.CodeDom.Compiler.GeneratedCodeAttribute(\"System.Resources.Tools.StronglyTypedResourceBuilder\", \"4.0.0.0\"),  _",
        "\t Global.System.Diagnostics.DebuggerNonUserCodeAttribute(),  _",
        "\t Global.System.Runtime.CompilerServices.CompilerGeneratedAttribute(),  _",
        "\t Global.Microsoft.VisualBasic.HideModuleNameAttribute()>  _",
        "\tFriend Module Resources",
        "",
        "\t\tPrivate resourceMan As Global.System.Resources.ResourceManager",
        "",
        "\t\tPrivate resourceCulture As Global.System.Globalization.CultureInfo",
        "",
        "\t\t'''<summary>",
        "\t\t'''  Returns the cached ResourceManager instance used by this class.",
        "\t\t'''</summary>",
        "\t\t<Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _",
        "\t\tFriend ReadOnly Property ResourceManager() As Global.System.Resources.ResourceManager",
        "\t\t\tGet",
        "\t\t\t\tIf Object.ReferenceEquals(resourceMan, Nothing) Then",
        $"\t\t\t\t\tDim temp As Global.System.Resources.ResourceManager = New Global.System.Resources.ResourceManager(\"{rootNamespace}.Resources\", GetType(Resources).Assembly)",
        "\t\t\t\t\tresourceMan = temp",
        "\t\t\t\tEnd If",
        "\t\t\t\tReturn resourceMan",
        "\t\t\tEnd Get",
        "\t\tEnd Property",
        "",
        "\t\t'''<summary>",
        "\t\t'''  Overrides the current thread's CurrentUICulture property for all",
        "\t\t'''  resource lookups using this strongly typed resource class.",
        "\t\t'''</summary>",
        "\t\t<Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _",
        "\t\tFriend Property Culture() As Global.System.Globalization.CultureInfo",
        "\t\t\tGet",
        "\t\t\t\tReturn resourceCulture",
        "\t\t\tEnd Get",
        "\t\t\tSet",
        "\t\t\t\tresourceCulture = value",
        "\t\t\tEnd Set",
        "\t\tEnd Property",
        "\tEnd Module",
        "End Namespace",
        "",
    ]);

    [Fact]
    public void ImportEditsARealisticVisualStudioAuthoredDesignerVbIndentedWithTabsAndCrlf()
    {
        var project = WriteProject("App/App.vbproj", Vbproj("Contoso.App"));
        WriteProject("App/My Project/Resources.Designer.vb", TabIndentedDesignerVb("Contoso.App"));
        var png = WritePng("logo.png");

        var entry = WinFormsProjectResources.Import(project, png, isIcon: false);

        Assert.Equal("logo", entry.Name);
        var designerText = File.ReadAllText(WinFormsProjectResources.ResourcesDesignerVbPath(project));
        Assert.Contains("Friend ReadOnly Property logo() As System.Drawing.Bitmap", designerText, StringComparison.Ordinal);
        Assert.Contains(
            "Return CType(ResourceManager.GetObject(\"logo\", resourceCulture), System.Drawing.Bitmap)",
            designerText, StringComparison.Ordinal);
        // The new property is inserted inside the module, not appended after it.
        Assert.True(designerText.IndexOf("Friend ReadOnly Property logo()", StringComparison.Ordinal)
            < designerText.LastIndexOf("End Module", StringComparison.Ordinal));
        // The new property's own indentation matches its tab-indented neighbours, rather
        // than the four spaces this would have hard-coded before this fix.
        Assert.Contains("\t\tFriend ReadOnly Property logo()", designerText, StringComparison.Ordinal);
        // Still compiles as the same module: every pre-existing member survives untouched.
        Assert.Contains("\t\tFriend Property Culture() As Global.System.Globalization.CultureInfo", designerText, StringComparison.Ordinal);
    }

    // ---- Import: a malformed Resources.Designer.vb is rejected, changing nothing ----------

    [Fact]
    public void ImportThrowsAndChangesNothingWhenDesignerVbHasNoClosingEndModule()
    {
        var project = WriteProject("App/App.vbproj", Vbproj("Contoso.App"));
        const string malformedDesigner = """
            Namespace My.Resources
                Friend Module Resources
                    ' Hand-edited: the closing "End Module" was deleted by mistake.
            End Namespace
            """;
        var designerPath = WriteProject("App/My Project/Resources.Designer.vb", malformedDesigner);
        var png = WritePng("logo.png");

        Assert.Throws<FormatException>(() => WinFormsProjectResources.Import(project, png, isIcon: false));

        // Nothing was written: not the resx, not the copied file, not the project items,
        // and not the malformed Designer.vb itself.
        Assert.Equal(malformedDesigner, File.ReadAllText(designerPath));
        Assert.False(File.Exists(WinFormsProjectResources.ResourcesResxPath(project)));
        Assert.False(Directory.Exists(WinFormsProjectResources.ImportedFilesDirectory(project)));
        Assert.DoesNotContain("VbMyResourcesResXFileCodeGenerator", File.ReadAllText(project), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportThrowsAndChangesNothingWhenResourcesResxHasTheWrongRootElement()
    {
        var project = WriteProject("App/App.vbproj", Vbproj("Contoso.App"));
        // Hand-edited into something that is no longer a resx at all, not merely
        // reformatted: well-formed XML, but not the document Import can edit.
        const string malformedResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <resources>
              <data name="Old"><value>1</value></data>
            </resources>
            """;
        var resxPath = WriteProject("App/My Project/Resources.resx", malformedResx);
        var png = WritePng("logo.png");

        Assert.Throws<FormatException>(() => WinFormsProjectResources.Import(project, png, isIcon: false));

        // Nothing was written: not Resources.Designer.vb, not the copied file, not the
        // malformed resx itself, which is left exactly as it was found.
        Assert.Equal(malformedResx, File.ReadAllText(resxPath));
        Assert.False(File.Exists(WinFormsProjectResources.ResourcesDesignerVbPath(project)));
        Assert.False(Directory.Exists(WinFormsProjectResources.ImportedFilesDirectory(project)));
    }
}
