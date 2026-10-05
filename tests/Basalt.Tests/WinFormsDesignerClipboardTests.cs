using Basalt.Shell.Controls;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

public sealed class WinFormsDesignerClipboardTests
{
    [Fact]
    public void PastingAContainerCopiesItsHierarchyAndExpressionsWithUniqueFieldNames()
    {
        var original = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .SetProperty("Button1", "Name", "\"Button1\"");
        var document = new WinFormsDesignerDocument(original);
        var clipboard = document.CopyControls(["Panel1", "Button1"]);

        var paste = document.PasteControls(clipboard, "Me");
        var updated = new WinFormsDesignerDocument(paste.Source);

        Assert.Equal(["Panel2"], paste.TopLevelNames);
        Assert.Equal(["Panel2", "Button2"], paste.AllNames);
        Assert.Equal("Panel2", Assert.Single(updated.Controls, control => control.Name == "Button2").ParentName);
        Assert.Equal("\"Before\"", updated.Controls.Single(control => control.Name == "Button2").Properties["Text"]);
        Assert.Equal("DangerousCall()", updated.Controls.Single(control => control.Name == "Button2").Properties["Tag"]);
        Assert.Equal("\"Button2\"", updated.Controls.Single(control => control.Name == "Button2").Properties["Name"]);
        Assert.Contains("Me.Panel2.Controls.Add(Me.Button2)", paste.Source);
        Assert.Contains("Me.Controls.Add(Me.Panel2)", paste.Source);
        Assert.Contains("Handles Button1.Click", paste.Source);
        Assert.DoesNotContain("Handles Button2.Click", paste.Source);
        Assert.Equal(original, document.Source);
        AssertCompiles(paste.Source);
    }

    [Fact]
    public void ExternalComponentReferencesMustExistInTheTargetBeforePasting()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddComponent("Images1", "Global.System.Windows.Forms.ImageList");
        source = new WinFormsDesignerDocument(source).SetProperty("Button1", "Tag", "Me.Images1");
        var clipboard = new WinFormsDesignerDocument(source).CopyControls(["Button1"]);
        var target = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);

        Assert.Throws<NotSupportedException>(() => target.PasteControls(clipboard, "Panel1"));
        Assert.Equal(WinFormsDesignerDocumentTests.Source, target.Source);

        var compatible = new WinFormsDesignerDocument(target.AddComponent("Images1",
            "Global.System.Windows.Forms.ImageList"));
        var paste = compatible.PasteControls(clipboard, "Panel1");
        var added = Assert.Single(new WinFormsDesignerDocument(paste.Source).Controls,
            control => control.Name == "Button2");
        Assert.Equal("Me.Images1", added.Properties["Tag"]);
        AssertCompiles(paste.Source);
    }

    [Fact]
    public void ReferencesBetweenCopiedControlsAreRemappedInExpressionSyntax()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .SetProperty("Button1", "Tag", "Me.Panel1");
        var document = new WinFormsDesignerDocument(source);
        var clipboard = document.CopyControls(["Panel1"]);

        var paste = document.PasteControls(clipboard, "Me");

        var button = Assert.Single(new WinFormsDesignerDocument(paste.Source).Controls,
            control => control.Name == "Button2");
        Assert.Equal("Me.Panel2", button.Properties["Tag"]);
        AssertCompiles(paste.Source);
    }

    [Fact]
    public void QualifiedMethodReferencesRequireTheTargetMethodBeforePasting()
    {
        const string uniqueMethod = """
                Private Function UniqueMethod() As Object
                    Return Nothing
                End Function
            """;
        var source = WinFormsDesignerDocumentTests.Source.Replace(
            "End Class", uniqueMethod + "\nEnd Class", StringComparison.Ordinal);
        source = new WinFormsDesignerDocument(source).SetProperty(
            "Button1", "Tag", "Me.UniqueMethod()");
        var clipboard = new WinFormsDesignerDocument(source).CopyControls(["Button1"]);
        var target = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);

        Assert.Throws<NotSupportedException>(() => target.PasteControls(clipboard, "Panel1"));
        Assert.Equal(WinFormsDesignerDocumentTests.Source, target.Source);

        var compatible = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source.Replace(
            "End Class", uniqueMethod + "\nEnd Class", StringComparison.Ordinal));
        var paste = compatible.PasteControls(clipboard, "Panel1");
        Assert.Contains("Me.Button2.Tag = Me.UniqueMethod()", paste.Source);
        AssertCompiles(paste.Source);
    }

    [Fact]
    public void PastingADataGridClonesItsColumnFieldsAndCollectionExpressions()
    {
        var source = new WinFormsDesignerDocument(WinFormsCollectionDocumentTests.Source);
        var clipboard = source.CopyControls(["Grid1"]);
        var target = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);

        var paste = target.PasteControls(clipboard, "Me");
        var reopened = new WinFormsDesignerDocument(paste.Source);

        Assert.Equal(["Grid1"], paste.TopLevelNames);
        Assert.Equal(["Grid1", "TitleColumn", "CheckColumn"], paste.AllNames);
        Assert.Contains("Me.Grid1.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() " +
            "{Me.TitleColumn, Me.CheckColumn})", paste.Source);
        Assert.Contains("Me.TitleColumn.DataPropertyName = \"TitleData\"", paste.Source);
        Assert.Single(reopened.Controls, control => control.Name == "Grid1");
        AssertCompiles(paste.Source);

        var duplicate = new WinFormsDesignerDocument(paste.Source).PasteControls(clipboard, "Me");
        Assert.Equal(["Grid2", "TitleColumn1", "CheckColumn1"], duplicate.AllNames);
        Assert.Contains("{Me.TitleColumn1, Me.CheckColumn1}", duplicate.Source);
        AssertCompiles(duplicate.Source);
    }

    [Fact]
    public void EventSubscriptionsInInitializeComponentStayWithTheOriginalControl()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace(
            "Me.Panel1.Controls.Add(Me.Button1)",
            "AddHandler Me.Button1.Click, AddressOf Button1_Click\n        Me.Panel1.Controls.Add(Me.Button1)",
            StringComparison.Ordinal);
        var document = new WinFormsDesignerDocument(source);

        var pasted = document.PasteControls(document.CopyControls(["Panel1"]), "Me");

        Assert.Contains("AddHandler Me.Button1.Click", pasted.Source);
        Assert.DoesNotContain("AddHandler Me.Button2.Click", pasted.Source);
        Assert.DoesNotContain("Handles Button2.Click", pasted.Source);
        AssertCompiles(pasted.Source);
    }

    [Fact]
    public async Task CrossDocumentPasteIsOneUndoableSessionEditAndNeverExecutesPropertyCode()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("MainForm", "SourceForm", StringComparison.Ordinal);
        using var sourceSession = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        var clipboard = sourceSession.CopyControls([sourceSession.FindControl("Panel1")!]);
        using var target = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var original = target.Document.Source;

        var added = await target.PasteControlsAsync(clipboard, target.Form);

        Assert.Equal(["Panel2"], added);
        Assert.Same(target.FindControl("Panel2"), target.FindControl("Button2")!.Parent);
        Assert.Equal("Before", target.FindControl("Button2")!.Text);
        Assert.Contains(target.PreviewProblems, problem => problem.StartsWith("Button2.Tag", StringComparison.Ordinal));
        AssertCompiles(target.Document.Source);

        await target.UndoAsync();
        Assert.Equal(original, target.Document.Source);
        Assert.Null(target.FindControl("Panel2"));
        await target.RedoAsync();
        Assert.NotNull(target.FindControl("Panel2"));
        Assert.NotNull(target.FindControl("Button2"));
    }

    private static void AssertCompiles(string source)
    {
        var platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var paths = platformPaths.Concat(
            [typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = VisualBasicCompilation.Create("DesignerClipboardRegression",
            [VisualBasicSyntaxTree.ParseText(source)],
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
