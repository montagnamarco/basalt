using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsDesignerResourceOperationsTests
{
    [Fact]
    public void ReadsTrustedResourceCallsAndDirectAssignmentsInInitializeComponentOrder()
    {
        const string source = """
            Partial Class MainForm
                Inherits System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Dim resources As New Global.System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                    Me.Button1 = New Global.System.Windows.Forms.Button()
                    resources.ApplyResources(Me.Button1, "Button1")
                    Me.Button1.Text = "Override"
                    Me.Button1.Image = CType(resources.GetObject("Button1.Image"), System.Drawing.Image)
                    Me.Button1.Tag = DirectCast(resources.GetObject("Button1.Tag"), Object)
                    Button1.AccessibleName = "Bare field"
                    Call resources.ApplyResources(Button1, "Button1Bare")
                    Me.Text = resources.GetString("$this.Text")
                    resources.ApplyResources(Me, "$this")
                    Me.Controls.Add(Me.Button1)
                End Sub
                Friend WithEvents Button1 As System.Windows.Forms.Button
            End Class
            """;

        var operations = new WinFormsDesignerDocument(source).ResourceOperations;

        Assert.Collection(operations,
            operation => AssertOperation(operation, "Button1", null, "Button1", null),
            operation => AssertOperation(operation, "Button1", "Text", null, "\"Override\""),
            operation => AssertOperation(operation, "Button1", "Image", "Button1.Image",
                "CType(resources.GetObject(\"Button1.Image\"), System.Drawing.Image)"),
            operation => AssertOperation(operation, "Button1", "Tag", "Button1.Tag",
                "DirectCast(resources.GetObject(\"Button1.Tag\"), Object)"),
            operation => AssertOperation(operation, "Button1", "AccessibleName", null, "\"Bare field\""),
            operation => AssertOperation(operation, "Button1", null, "Button1Bare", null),
            operation => AssertOperation(operation, "Me", "Text", "$this.Text",
                "resources.GetString(\"$this.Text\")"),
            operation => AssertOperation(operation, "Me", null, "$this", null));
    }

    [Fact]
    public void IgnoresUntrustedManagersAndUnrelatedMethods()
    {
        const string source = """
            Imports System.ComponentModel
            Namespace Demo
                Partial Class MainForm
                    Inherits System.Windows.Forms.Form
                    Private Sub InitializeComponent()
                        Dim wrong As New Global.System.ComponentModel.ComponentResourceManager(GetType(String))
                        Dim shortName As New ComponentResourceManager(GetType(MainForm))
                        Dim resources As New System.ComponentModel.ComponentResourceManager(GetType(Demo.MainForm))
                        Me.Button1 = New Global.System.Windows.Forms.Button()
                        wrong.ApplyResources(Me.Button1, "Wrong")
                        shortName.ApplyResources(Me.Button1, "UnprovedAlias")
                        resources.ApplyResources(Me.Button1, "Trusted")
                        resources = Nothing
                        resources.ApplyResources(Me.Button1, "AfterReassignment")
                        Me.Button1.Text = wrong.GetString("Wrong.Text")
                        Me.Controls.Add(Me.Button1)
                    End Sub
                    Private Sub UserHelper()
                        Dim resources As New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                        resources.ApplyResources(Me.Button1, "FromHelper")
                    End Sub
                    Friend WithEvents Button1 As System.Windows.Forms.Button
                End Class
            End Namespace
            """;

        var operations = new WinFormsDesignerDocument(source).ResourceOperations;

        Assert.Collection(operations,
            operation => AssertOperation(operation, "Button1", null, "Trusted", null),
            operation => AssertOperation(operation, "Button1", "Text", null,
                "wrong.GetString(\"Wrong.Text\")"));
    }

    [Fact]
    public void BareControlNameDoesNotResolveWhenShadowedByALocal()
    {
        const string source = """
            Partial Class MainForm
                Inherits System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Me.Button1 = New Global.System.Windows.Forms.Button()
                    Dim Button1 As New Global.System.Windows.Forms.Button()
                    Button1.Text = "Local only"
                    Me.Button1.Text = "Field"
                    Me.Controls.Add(Me.Button1)
                End Sub
                Friend WithEvents Button1 As System.Windows.Forms.Button
            End Class
            """;

        var operation = Assert.Single(new WinFormsDesignerDocument(source).ResourceOperations);
        AssertOperation(operation, "Button1", "Text", null, "\"Field\"");
    }

    private static void AssertOperation(WinFormsDesignerResourceOperation operation,
        string control, string? property, string? key, string? expression)
    {
        Assert.Equal(control, operation.ControlName);
        Assert.Equal(property, operation.PropertyName);
        Assert.Equal(key, operation.ResourceKey);
        Assert.Equal(expression, operation.Expression?.ToString());
    }
}
