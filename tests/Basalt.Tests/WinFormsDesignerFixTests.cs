using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Designer;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Designer defects a live probe found on a fresh New Project WinForms app. Each test reproduces one of them on the
/// project the template writes, not on a hand-built document, so the template's own references are part of the test.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsDesignerFixTests
{
    [Fact]
    public async Task RenamingAControlInAFreshWinFormsProjectRenamesItsFieldAndEveryReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "basalt-forms-rename-" + Guid.NewGuid().ToString("N"));
        try
        {
            var generated = await SolutionTemplates.CreateAsync(root, "RenameForms", ProjectTemplate.WinFormsApp);
            var designerPath = generated.MainFormDesignerPath!;
            var companionPath = Path.Combine(Path.GetDirectoryName(designerPath)!, "MainForm.vb");
            var source = new WinFormsDesignerDocument(await File.ReadAllTextAsync(designerPath))
                .AddControl("Button1", "Global.System.Windows.Forms.Button");
            await File.WriteAllTextAsync(designerPath, source);
            var companion = (await File.ReadAllTextAsync(companionPath)).Replace("End Class",
                "    Private Sub OnOk(sender As Object, e As System.EventArgs) Handles Button1.Click\n    End Sub\nEnd Class",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(companionPath, companion);

            using var language = new RoslynLanguageService();
            await language.OpenSolutionAsync(generated.ProjectPath);
            var position = new WinFormsDesignerDocument(source).FieldPosition("Button1");

            var preview = await language.PreviewRenameAsync(designerPath, position, "btnOk");

            Assert.True(preview.CanApply, preview.Problem);
            var designer = preview.Changes.Single(change => change.FilePath.EndsWith("MainForm.Designer.vb", StringComparison.Ordinal));
            var code = preview.Changes.Single(change => change.FilePath.EndsWith("MainForm.vb", StringComparison.Ordinal));
            Assert.Contains("Friend WithEvents btnOk As", designer.NewText, StringComparison.Ordinal);
            Assert.Contains("Me.btnOk.", designer.NewText, StringComparison.Ordinal);
            Assert.Contains("Handles btnOk.Click", code.NewText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DroppedControlsStartWithVisualStudiosDefaultsAtTheDropPoint()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Dropped1", "Global.System.Windows.Forms.Button", "Me", (150, 80));
        source = new WinFormsDesignerDocument(source).AddControl("Dropped2", "Global.System.Windows.Forms.TextBox");

        Assert.Contains("Me.Dropped1.Location = New System.Drawing.Point(150, 80)", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped1.Size = New System.Drawing.Size(75, 23)", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped1.Text = \"Dropped1\"", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped1.TabIndex = 0", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped2.Size = New System.Drawing.Size(100, 23)", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped2.TabIndex = 1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Me.Dropped2.Text", source, StringComparison.Ordinal);
        Assert.DoesNotContain("New System.Drawing.Size(100, 30)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ATabControlBringsTwoTabPagesAsControlsOfItsOwn()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("TabControl1", "Global.System.Windows.Forms.TabControl");
        var document = new WinFormsDesignerDocument(source);

        Assert.Contains("Me.TabControl1.Controls.Add(Me.TabPage1)", source, StringComparison.Ordinal);
        Assert.Contains("Me.TabControl1.Controls.Add(Me.TabPage2)", source, StringComparison.Ordinal);
        Assert.Contains("Me.TabPage1.Text = \"TabPage1\"", source, StringComparison.Ordinal);
        Assert.Contains("Friend WithEvents TabPage2 As Global.System.Windows.Forms.TabPage", source, StringComparison.Ordinal);
        Assert.Contains(document.Controls, control => control.Name == "TabPage2" && control.ParentName == "TabControl1");
    }

    [Fact]
    public void PropertiesAreWrittenBeforeTheControlIsAddedToItsParent()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Dropped1", "Global.System.Windows.Forms.Button");
        source = new WinFormsDesignerDocument(source).SetProperty("Dropped1", "Tag", "\"later\"");

        var tag = source.IndexOf("Me.Dropped1.Tag = \"later\"", StringComparison.Ordinal);
        var add = source.IndexOf("Me.Controls.Add(Me.Dropped1)", StringComparison.Ordinal);
        Assert.True(tag >= 0 && add > tag, "The new property must come before Controls.Add(Me.Button1).");
    }

    [AvaloniaFact]
    public void TabControlSelectedIndexIsWrittenAfterItsPagesAreAdded()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("TabControl1", "Global.System.Windows.Forms.TabControl");
        source = new WinFormsDesignerDocument(source).SetProperty("TabControl1", "SelectedIndex", "1");

        // Visual Studio writes the pages first: a SelectedIndex written before them names a page that does not exist yet.
        var lastPage = source.IndexOf("Me.TabControl1.Controls.Add(Me.TabPage2)", StringComparison.Ordinal);
        var selected = source.IndexOf("Me.TabControl1.SelectedIndex = 1", StringComparison.Ordinal);
        Assert.True(lastPage >= 0 && selected > lastPage, "SelectedIndex must come after the TabPages are added.");

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        Assert.DoesNotContain(session.PreviewProblems, problem => problem.Contains("SelectedIndex", StringComparison.Ordinal));
        Assert.Equal(1, ((Forms.TabControl)session.FindControl("TabControl1")!).SelectedIndex);
    }

    [Fact]
    public void TabControlSelectedIndexFollowsPagesAddedThroughTabPages()
    {
        // A hand-written file adds its pages with TabPages.Add, which the designer reads as adding children too.
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("TabControl1", "Global.System.Windows.Forms.TabControl");
        source = source.Replace("Me.TabControl1.Controls.Add(Me.TabPage2)", "Me.TabControl1.TabPages.Add(Me.TabPage2)", StringComparison.Ordinal);
        source = new WinFormsDesignerDocument(source).SetProperty("TabControl1", "SelectedIndex", "1");

        var lastPage = source.IndexOf("Me.TabControl1.TabPages.Add(Me.TabPage2)", StringComparison.Ordinal);
        var selected = source.IndexOf("Me.TabControl1.SelectedIndex = 1", StringComparison.Ordinal);
        Assert.True(lastPage >= 0 && selected > lastPage, "SelectedIndex must come after the pages added through TabPages.Add.");
    }

    [Fact]
    public void DataGridViewDefaultEventIsCellContentClick()
    {
        var method = typeof(WinFormsDesignerView).GetMethod("DefaultEventOf", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var grid = new Forms.DataGridView();

        var descriptor = (EventDescriptor?)method.Invoke(null, [grid]);

        Assert.Equal("CellContentClick", descriptor?.Name);
    }

    [Fact]
    public async Task RenamingAFriendControlIsAllowedWhenACSharpProjectUsesTheVisualBasicProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "basalt-forms-rename-blocked-" + Guid.NewGuid().ToString("N"));
        try
        {
            var generated = await SolutionTemplates.CreateAsync(root, "RenameForms", ProjectTemplate.WinFormsApp);
            var designerPath = generated.MainFormDesignerPath!;
            var source = new WinFormsDesignerDocument(await File.ReadAllTextAsync(designerPath))
                .AddControl("Button1", "Global.System.Windows.Forms.Button");
            await File.WriteAllTextAsync(designerPath, source);

            // A C# project references the Visual Basic project, but Button1 is Friend: no code outside the project can name it, so the rename is not blocked.
            var consumerDirectory = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(consumerDirectory);
            var vbProject = Path.GetRelativePath(root, generated.ProjectPath).Replace('/', '\\');
            var vbReference = Path.GetRelativePath(consumerDirectory, generated.ProjectPath);
            await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{vbReference}" />
                  </ItemGroup>
                </Project>
                """);
            var companionPath = Path.Combine(Path.GetDirectoryName(designerPath)!, "MainForm.vb");
            var companion = (await File.ReadAllTextAsync(companionPath)).Replace("End Class",
                "    Private Sub OnOk(sender As Object, e As System.EventArgs) Handles Button1.Click\n    End Sub\nEnd Class",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(companionPath, companion);
            var solutionPath = Path.Combine(root, "Blocked.sln");
            await File.WriteAllTextAsync(solutionPath, string.Join('\n',
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                $"Project(\"{{F184B08F-C81C-45F6-A57F-5ABD9991F28F}}\") = \"RenameForms\", \"{vbProject}\", \"{{6F1C3E0A-1B2D-4C5E-8F90-A1B2C3D4E5F6}}\"",
                "EndProject",
                "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"Consumer\", \"Consumer\\Consumer.csproj\", \"{7A2D4F1B-2C3E-4D5F-9A01-B2C3D4E5F601}\"",
                "EndProject",
                ""));

            using var language = new RoslynLanguageService();
            await language.OpenSolutionAsync(solutionPath);
            var position = new WinFormsDesignerDocument(source).FieldPosition("Button1");

            var preview = await language.PreviewRenameAsync(designerPath, position, "btnOk");

            Assert.True(preview.CanApply, preview.Problem);
            Assert.True(await RoslynLanguageService.ApplyAsync(preview));

            var designer = await File.ReadAllTextAsync(designerPath);
            Assert.Contains("Friend WithEvents btnOk As", designer, StringComparison.Ordinal);
            Assert.Contains("Me.btnOk.", designer, StringComparison.Ordinal);
            // The designer's Name property still reads "Button1"; only the field's own references must follow the rename.
            Assert.DoesNotContain("Me.Button1.", designer, StringComparison.Ordinal);
            Assert.DoesNotContain("WithEvents Button1 ", designer, StringComparison.Ordinal);
            Assert.Contains("Handles btnOk.Click", await File.ReadAllTextAsync(companionPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ATabControlsPagesAndTheControlsAfterItAreNumberedWithinTheirContainers()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("TabControl1", "Global.System.Windows.Forms.TabControl");
        source = new WinFormsDesignerDocument(source).AddControl("Dropped1", "Global.System.Windows.Forms.Button");

        Assert.Contains("Me.TabPage1.TabIndex = 0", source, StringComparison.Ordinal);
        Assert.Contains("Me.TabPage2.TabIndex = 1", source, StringComparison.Ordinal);
        Assert.Contains("Me.Dropped1.TabIndex = 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DockedStripsAreWrittenAtTheirEdgeAcrossTheFormWidth()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("MenuStrip1", "Global.System.Windows.Forms.MenuStrip");
        source = new WinFormsDesignerDocument(source).AddControl("StatusStrip1", "Global.System.Windows.Forms.StatusStrip");

        // The fixture's form is 640 by 480: a MenuStrip is 24 high at the top, a StatusStrip 22 high at the bottom.
        Assert.Contains("Me.MenuStrip1.Location = New System.Drawing.Point(0, 0)", source, StringComparison.Ordinal);
        Assert.Contains("Me.MenuStrip1.Size = New System.Drawing.Size(640, 24)", source, StringComparison.Ordinal);
        Assert.Contains("Me.StatusStrip1.Location = New System.Drawing.Point(0, 458)", source, StringComparison.Ordinal);
        Assert.Contains("Me.StatusStrip1.Size = New System.Drawing.Size(640, 22)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AWebBrowserStartsAt250BySquare()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("WebBrowser1", "Global.System.Windows.Forms.WebBrowser");

        Assert.Contains("Me.WebBrowser1.Size = New System.Drawing.Size(250, 250)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AFontKeepsItsCharacterSetWhenItIsWrittenAgain()
    {
        // A file's own CType(1, Byte) must survive a round trip through the designer unchanged.
        using var font = new System.Drawing.Font("Tahoma", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 1, false);
        var text = WinFormsDesignerValues.Serialize(font);

        Assert.Contains("CType(1, Byte)", text, StringComparison.Ordinal);
        using var again = Assert.IsType<System.Drawing.Font>(WinFormsDesignerValues.Evaluate(
            Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory.ParseExpression(text), typeof(System.Drawing.Font)));
        Assert.Equal(1, again.GdiCharSet);
    }

    [Fact]
    public void AFontEditedFromDefaultCharSetIsWrittenWithAnsi()
    {
        // The dialog gives back a font with the character set it was given: DEFAULT_CHARSET (1) is replaced by ANSI (0), which
        // is what Visual Studio writes for the font it resolved. Any other character set stays.
        using var defaults = new System.Drawing.Font("Tahoma", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 1, false);
        using var edited = WinFormsDesignerValues.AsEditedFont(defaults);
        using var japanese = new System.Drawing.Font("Tahoma", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 128, false);

        Assert.Equal(0, edited.GdiCharSet);
        Assert.Contains("CType(0, Byte)", WinFormsDesignerValues.Serialize(edited), StringComparison.Ordinal);
        Assert.Same(japanese, WinFormsDesignerValues.AsEditedFont(japanese));
    }

    [Fact]
    public void AnEventStubNamesTheSystemTypesAsVisualBasicDoes()
    {
        const string companion = """
            Partial Class MainForm
            End Class
            """;

        var written = WinFormsEventHandlers.Set(companion, "MainForm", "Button1", "Click", "Button1_Click",
            typeof(System.EventHandler));
        var mouse = WinFormsEventHandlers.Set(companion, "MainForm", "Button1", "MouseDown", "Button1_MouseDown",
            typeof(System.Windows.Forms.MouseEventHandler));

        Assert.Contains("Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click", written, StringComparison.Ordinal);
        Assert.Contains("e As Global.System.Windows.Forms.MouseEventArgs", mouse, StringComparison.Ordinal);
    }

    [Fact]
    public void ALiteralConcatenationPreviewsAsTheJoinedText()
    {
        var source = WinFormsDesignerDocumentTests.Source
            .Replace("Me.Button1.Text = \"Before\"", "Me.Button1.Text = \"A\" & \"B\"", StringComparison.Ordinal)
            .Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = Nothing", StringComparison.Ordinal);

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        Assert.Empty(session.PreviewProblems);
        Assert.Equal("AB", session.FindControl("Button1")!.Text);
    }

    [AvaloniaFact]
    public void ALineContinuationKeepsAnExpressionOnTheNextLine()
    {
        var source = WinFormsDesignerDocumentTests.Source
            .Replace("Me.Button1.Text = \"Before\"", "Me.Button1.Text = \"A\" & _\n                \"B\"", StringComparison.Ordinal)
            .Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = Nothing", StringComparison.Ordinal);

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        // The continuation joins the two lines into one expression: its value is the joined text, and nothing is left unpreviewed.
        Assert.Empty(session.PreviewProblems);
        Assert.Equal("AB", session.FindControl("Button1")!.Text);
    }

    [AvaloniaFact]
    public void AnOperatorEndingItsLineContinuesOntoTheNextLine()
    {
        // Visual Basic continues an expression after a binary operator at the end of a line, so this is valid designer code.
        var source = WinFormsDesignerDocumentTests.Source
            .Replace("Me.Button1.Text = \"Before\"", "Me.Button1.Text = \"A\" &\n                \"B\"", StringComparison.Ordinal)
            .Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = Nothing", StringComparison.Ordinal);

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        Assert.Empty(session.PreviewProblems);
        Assert.Equal("AB", session.FindControl("Button1")!.Text);
    }

    [Fact]
    public void ASyntaxErrorIsRefusedWithTheLineAndColumnItIsOn()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = )",
            StringComparison.Ordinal);
        var line = LineOf(source, "Me.Button1.Tag = )");

        var error = Assert.Throws<FormatException>(() => new WinFormsDesignerDocument(source));

        Assert.Contains($"line {line}, column", error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ADesignerWithASyntaxErrorShowsTheLineInItsStatus()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = )",
            StringComparison.Ordinal);
        var line = LineOf(source, "Me.Button1.Tag = )");
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            bool ShowsLine(TextBlock block) => block.Text?.Contains($"line {line}, column", StringComparison.Ordinal) == true;
            await WaitUntilAsync(() => view.GetVisualDescendants().OfType<TextBlock>().Any(ShowsLine));

            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), ShowsLine);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ZoomToFitShrinksAFormLargerThanTheViewOfTheDesigner()
    {
        // The fixture's form is 640 by 480, in a 400 by 300 window: the view is smaller than the form at 100 percent.
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 400, Height = 300 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null && surface.Bounds.Width > 0);

            surface.ZoomToFit();

            Assert.True(surface.Zoom < 1, $"A form larger than the view should shrink; zoom was {surface.Zoom}.");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    /// <summary>The one-based line on which <paramref name="fragment"/> first appears in <paramref name="source"/>.</summary>
    private static int LineOf(string source, string fragment) =>
        source[..source.IndexOf(fragment, StringComparison.Ordinal)].Count(character => character == '\n') + 1;
}
