using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

public class WinFormsDesignerRenameTests
{
    [AvaloniaFact]
    public async Task SemanticFieldRenamePreservesShadowedLocalsAndLinksUndo()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = Nothing", StringComparison.Ordinal);
        const string companion = """
            Partial Class MainForm
                Private Sub SaveAction(sender As Object, e As System.EventArgs) Handles Button1.Click
                    Dim Button1 As String = "Button1"
                    Me.Button1.Text = Button1
                End Sub
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("DesignerRename", LanguageNames.VisualBasic)
            .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location]).Distinct();
        project = project.AddMetadataReferences(paths.Select(path => MetadataReference.CreateFromFile(path)));
        project = project.AddDocument("MainForm.Designer.vb", SourceText.From(source), filePath: "/tmp/MainForm.Designer.vb").Project;
        project = project.AddDocument("MainForm.vb", SourceText.From(companion), filePath: "/tmp/MainForm.vb").Project;
        var document = new WinFormsDesignerDocument(source);
        var preview = await new RenameRefactoring(project.Solution).PreviewAsync("/tmp/MainForm.Designer.vb", document.FieldPosition("Button1"), "SaveButton");
        Assert.True(preview.CanApply, preview.Problem);
        var renamed = preview.Changes.Single(change => change.FilePath.EndsWith(".Designer.vb", StringComparison.Ordinal)).NewText;
        var code = preview.Changes.Single(change => change.FilePath.EndsWith("MainForm.vb", StringComparison.Ordinal)).NewText;
        Assert.Contains("Handles SaveButton.Click", code);
        Assert.Contains("Dim Button1 As String = \"Button1\"", code);
        Assert.Contains("Me.SaveButton.Text = Button1", code);
        Assert.Contains("Sub SaveAction", code);
        WinFormsCompilationAssert.AssertCompiles(renamed + Environment.NewLine + code);
        var buffer = companion;
        using var session = new WinFormsDesignerSession(document);
        await session.ApplyFieldRenameAsync(renamed, "SaveButton", () => buffer = code, () => buffer = companion, () => buffer = code);
        Assert.Null(session.FindControl("Button1"));
        Assert.Equal("SaveButton", session.FindControl("SaveButton")!.Name);
        Assert.Equal(code, buffer);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.Equal(companion, buffer);
        await session.RedoAsync();
        Assert.Equal("SaveButton", session.FindControl("SaveButton")!.Name);
        Assert.Equal(code, buffer);
    }

    [Fact]
    public void RenameRejectsDuplicateAndInvalidNames()
    {
        var document = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);
        Assert.Throws<ArgumentException>(() => document.ValidateFieldRename("Button1", "Panel1"));
        Assert.Throws<ArgumentException>(() => document.ValidateFieldRename("Button1", "bad name"));
    }
}
