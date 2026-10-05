using Basalt.Workspace;

namespace Basalt.Tests;

public class WinFormsCollectionDocumentTests
{
    internal const string Source = """"
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ListBox1 = New System.Windows.Forms.ListBox()
                Me.ComboBox1 = New System.Windows.Forms.ComboBox()
                Me.Grid1 = New System.Windows.Forms.DataGridView()
                Me.TitleColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
                Me.CheckColumn = New System.Windows.Forms.DataGridViewCheckBoxColumn()
                Me.ListBox1.Items.AddRange(New Object() {"Alpha", "Quoted ""item"""}) ' item comment
                Me.ComboBox1.Items.Add("Choice")
                Me.TitleColumn.Name = "TitleColumn"
                Me.TitleColumn.HeaderText = "Title"
                Me.TitleColumn.Width = 120
                Me.TitleColumn.DataPropertyName = "TitleData" ' preserve column binding
                Me.CheckColumn.Name = "CheckColumn"
                Me.CheckColumn.HeaderText = "Enabled"
                Me.Grid1.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.TitleColumn, Me.CheckColumn}) ' column comment
                Me.Grid1.Tag = DangerousCall()
                Me.Controls.Add(Me.ListBox1)
                Me.Controls.Add(Me.ComboBox1)
                Me.Controls.Add(Me.Grid1)
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents ListBox1 As System.Windows.Forms.ListBox
            Friend WithEvents ComboBox1 As System.Windows.Forms.ComboBox
            Friend WithEvents Grid1 As System.Windows.Forms.DataGridView
            Friend WithEvents TitleColumn As System.Windows.Forms.DataGridViewTextBoxColumn
            Friend WithEvents CheckColumn As System.Windows.Forms.DataGridViewCheckBoxColumn
            Private Function DangerousCall() As Object
                Throw New System.InvalidOperationException("Never execute this")
            End Function
        End Class
        """";

    [Fact]
    public void ItemsRoundTripQuotesAndPreserveUnrelatedSourceAndComments()
    {
        var document = new WinFormsCollectionDocument(Source);
        Assert.Equal(["Alpha", "Quoted \"item\""], document.ReadItems("ListBox1").Items);
        var result = document.SetItems("ListBox1", ["Last", "A \"quote\"", "First"]);
        Assert.Equal(["Last", "A \"quote\"", "First"], new WinFormsCollectionDocument(result).ReadItems("ListBox1").Items);
        Assert.Contains("' item comment", result);
        Assert.Contains("Me.Grid1.Tag = DangerousCall()", result);
        Assert.Contains("Throw New System.InvalidOperationException(\"Never execute this\")", result);
        WinFormsCompilationAssert.AssertCompiles(result);
        var empty = new WinFormsCollectionDocument(result).SetItems("ListBox1", []);
        Assert.Empty(new WinFormsCollectionDocument(empty).ReadItems("ListBox1").Items);
        WinFormsCompilationAssert.AssertCompiles(empty);
    }

    [Fact]
    public void ColumnsReorderRenameAddAndRemovePersistRealFieldsAndKeepOtherProperties()
    {
        var document = new WinFormsCollectionDocument(Source);
        var columns = document.ReadColumns("Grid1").Columns;
        var renamed = columns[0] with { Name = "RenamedColumn", TypeName = "DataGridViewButtonColumn", HeaderText = "Renamed", Width = 145 };
        var result = document.SetColumns("Grid1", [columns[1], renamed,
            new("ButtonColumn", "DataGridViewButtonColumn", "Action", 90)]);
        var reopened = new WinFormsCollectionDocument(result).ReadColumns("Grid1");
        Assert.True(reopened.IsSupported);
        Assert.Equal(["CheckColumn", "RenamedColumn", "ButtonColumn"], reopened.Columns.Select(column => column.Name));
        Assert.Equal("DataGridViewButtonColumn", reopened.Columns[1].TypeName);
        Assert.Contains("Me.RenamedColumn.DataPropertyName = \"TitleData\" ' preserve column binding", result);
        Assert.Contains("' column comment", result);
        Assert.DoesNotContain("Friend WithEvents TitleColumn", result);
        Assert.Contains("Friend WithEvents ButtonColumn As Global.System.Windows.Forms.DataGridViewButtonColumn", result);
        WinFormsCompilationAssert.AssertCompiles(result);
        var deleted = new WinFormsCollectionDocument(result).SetColumns("Grid1", [reopened.Columns[0]]);
        Assert.DoesNotContain("Friend WithEvents RenamedColumn", deleted);
        Assert.DoesNotContain("Friend WithEvents ButtonColumn", deleted);
        WinFormsCompilationAssert.AssertCompiles(deleted);
    }

    [Fact]
    public void UnknownCollectionExpressionsAndUserColumnReferencesCannotBeErased()
    {
        var unknown = Source.Replace("\"Alpha\", \"Quoted \"\"item\"\"\"", "DangerousCall()", StringComparison.Ordinal);
        var document = new WinFormsCollectionDocument(unknown);
        Assert.False(document.ReadItems("ListBox1").IsSupported);
        Assert.Throws<NotSupportedException>(() => document.SetItems("ListBox1", ["Replacement"]));
        var referenced = Source.Replace("Throw New System.InvalidOperationException", "TitleColumn.Visible = False\n        Throw New System.InvalidOperationException", StringComparison.Ordinal);
        var guarded = new WinFormsCollectionDocument(referenced);
        Assert.Throws<NotSupportedException>(() => guarded.SetColumns("Grid1", []));
        var customHeader = Source.Replace("Me.TitleColumn.HeaderText = \"Title\"", "Me.TitleColumn.HeaderText = DangerousCall()", StringComparison.Ordinal);
        Assert.False(new WinFormsCollectionDocument(customHeader).ReadColumns("Grid1").IsSupported);
        var customType = Source.Replace("New System.Windows.Forms.DataGridViewTextBoxColumn()", "New ApplicationCode.DataGridViewTextBoxColumn()", StringComparison.Ordinal);
        Assert.False(new WinFormsCollectionDocument(customType).ReadColumns("Grid1").IsSupported);
    }
}
