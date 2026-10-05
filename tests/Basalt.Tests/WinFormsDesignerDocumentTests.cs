using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic;
using static Basalt.Tests.WinFormsCompilationAssert;

namespace Basalt.Tests;

public class WinFormsDesignerDocumentTests
{
    internal const string Source = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Panel1 = New Global.System.Windows.Forms.Panel()
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.Panel1.Size = New System.Drawing.Size(300, 200)
                Me.Button1.Location = New System.Drawing.Point(-5, 20)
                Me.Button1.Size = New System.Drawing.Size(90, 30)
                Me.Button1.Text = "Before" ' keep this comment
                Me.Button1.Tag = DangerousCall()
                Me.Panel1.Controls.Add(Me.Button1)
                Me.Controls.Add(Me.Panel1)
                Me.ClientSize = New System.Drawing.Size(640, 480)
                Me.Text = "Main"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Panel1 As System.Windows.Forms.Panel
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Private Sub Button1_Click(sender As Object, e As System.EventArgs) Handles Button1.Click
                ' Handler code stays untouched
                System.Console.WriteLine("Clicked")
            End Sub
            Private Function DangerousCall() As Object
                Throw New System.InvalidOperationException("User code must not execute in the designer")
            End Function
        End Class
        """;

    [Fact]
    public void ParsesControlHierarchyAndKeepsRootValueObjectsAsProperties()
    {
        var document = new WinFormsDesignerDocument(Source);
        Assert.Equal("MainForm", document.ClassName);
        Assert.Equal(2, document.Controls.Count);
        var button = Assert.Single(document.Controls, control => control.Name == "Button1");
        Assert.Equal("Panel1", button.ParentName);
        Assert.Equal("\"Before\"", button.Properties["Text"]);
        Assert.Equal("DangerousCall()", button.Properties["Tag"]);
        Assert.Equal("New System.Drawing.Size(640, 480)", document.Root.Properties["ClientSize"]);
    }

    [Fact]
    public void PropertyEditPreservesUnrelatedSyntaxAndUnknownExpressions()
    {
        var document = new WinFormsDesignerDocument(Source);
        var edited = document.SetProperty("Button1", "Text", "\"After\"");
        Assert.Equal(Source.Replace("\"Before\"", "\"After\"", StringComparison.Ordinal), edited);
        Assert.Contains("DangerousCall()", edited);
        Assert.Contains("Handles Button1.Click", edited);
        Assert.Contains("' keep this comment", edited);
        Assert.False(VisualBasicSyntaxTree.ParseText(edited).GetRoot().ContainsDiagnostics);
    }

    [Fact]
    public void AddedControlGoesBeforeTheFormResumeLayoutNotAContainerOne()
    {
        // The designer suspends and resumes each container; only the form's own
        // Me.ResumeLayout ends the initialization new statements belong to.
        var nested = Source.Replace(
            "Me.Controls.Add(Me.Panel1)",
            "Me.Controls.Add(Me.Panel1)\n        Me.Panel1.ResumeLayout(False)",
            StringComparison.Ordinal);
        Assert.NotEqual(Source, nested);
        var source = new WinFormsDesignerDocument(nested).AddControl("Label1", "Global.System.Windows.Forms.Label");
        var panelResume = source.IndexOf("Me.Panel1.ResumeLayout(False)", StringComparison.Ordinal);
        var labelCreation = source.IndexOf("Me.Label1 = New", StringComparison.Ordinal);
        var formResume = source.LastIndexOf("Me.ResumeLayout(False)", StringComparison.Ordinal);
        Assert.True(panelResume < labelCreation && labelCreation < formResume, source);
        AssertCompiles(source);
    }

    [Fact]
    public void AddsFieldsAndParentingThenDeletesNestedControls()
    {
        var source = new WinFormsDesignerDocument(Source).AddControl("Label1", "Global.System.Windows.Forms.Label", "Panel1");
        var document = new WinFormsDesignerDocument(source);
        Assert.Equal("Panel1", Assert.Single(document.Controls, control => control.Name == "Label1").ParentName);
        Assert.Contains("Friend WithEvents Label1 As Global.System.Windows.Forms.Label", source);
        Assert.False(VisualBasicSyntaxTree.ParseText(source).GetRoot().ContainsDiagnostics);
        AssertCompiles(source);

        var deleted = document.RemoveControl("Panel1");
        var afterDeletion = new WinFormsDesignerDocument(deleted);
        Assert.Empty(afterDeletion.Controls);
        Assert.DoesNotContain("Friend WithEvents Panel1", deleted);
        Assert.DoesNotContain("Friend WithEvents Button1", deleted);
        Assert.DoesNotContain("Friend WithEvents Label1", deleted);
        Assert.Contains("Handler code stays untouched", deleted);
        Assert.False(VisualBasicSyntaxTree.ParseText(deleted).GetRoot().ContainsDiagnostics);
        AssertCompiles(deleted);
    }

    [Fact]
    public void RejectsInvalidEditsAndRootDeletion()
    {
        var document = new WinFormsDesignerDocument(Source);
        Assert.Throws<ArgumentException>(() => document.SetProperty("Button1", "invalid name", "1"));
        Assert.Throws<FormatException>(() => document.SetProperty("Button1", "Text", "New ("));
        Assert.Throws<ArgumentException>(() => document.AddControl("button1", "System.Windows.Forms.Button"));
        Assert.Throws<ArgumentException>(() => document.RemoveControl("Me"));
        Assert.Equal(Source, document.Source);
    }

    [Fact]
    public void DeletingControlRetainsOtherHandlesBindingsAndHandlerBodies()
    {
        var source = Source.Replace("Handles Button1.Click", "Handles Button1.Click, Me.Load", StringComparison.Ordinal);
        var deleted = new WinFormsDesignerDocument(source).RemoveControl("Button1");
        Assert.DoesNotContain("Handles Button1.Click", deleted);
        Assert.Contains("Handles Me.Load", deleted);
        Assert.Contains("System.Console.WriteLine(\"Clicked\")", deleted);
        AssertCompiles(deleted);
    }
}
