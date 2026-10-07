using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsToolStripDocumentTests
{
    private const string ToolStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.Controls.Add(Me.ToolStrip1)
                ' Preserve unrelated application initialization.
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
        End Class
        """;

    private const string StatusStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.StatusStrip1 = New Global.System.Windows.Forms.StatusStrip()
                Me.StatusStrip1.Name = "StatusStrip1"
                Me.Controls.Add(Me.StatusStrip1)
            End Sub
            Friend WithEvents StatusStrip1 As Global.System.Windows.Forms.StatusStrip
        End Class
        """;

    // Every Designer.vb Visual Studio writes suspends layout around the
    // initialization; anything added after Me.ResumeLayout would run once
    // layout has already resumed, and Visual Studio never writes it there.
    private const string SuspendedLayoutSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.SuspendLayout()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.Controls.Add(Me.ToolStrip1)
                Me.ResumeLayout(False)
                Me.PerformLayout()
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
        End Class
        """;

    [Fact]
    public void ItemsAndTheirPropertiesAreWrittenBeforeTheFormResumesLayout()
    {
        var withButton = new WinFormsToolStripDocument(SuspendedLayoutSource).Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "&Save")]);
        var withProperty = new WinFormsToolStripDocument(withButton)
            .SetItemProperty("ToolStrip1", "ToolStripButton1", "ToolTipText", "\"Save\"");

        var resume = withProperty.IndexOf("Me.ResumeLayout(False)", StringComparison.Ordinal);

        Assert.InRange(withProperty.IndexOf("Me.ToolStripButton1 = New", StringComparison.Ordinal), 0, resume);
        Assert.InRange(withProperty.IndexOf("Me.ToolStrip1.Items.AddRange(", StringComparison.Ordinal), 0, resume);
        Assert.InRange(withProperty.IndexOf("Me.ToolStripButton1.ToolTipText = ", StringComparison.Ordinal), 0, resume);
    }

    [Fact]
    public void RemoveItemDeletesTheFieldStatementsAndMatchingHandlesClause()
    {
        var withItems = new WinFormsToolStripDocument(ToolStripSource).Set("ToolStrip1",
        [
            new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "&Save"),
            new WinFormsToolStripItem("ToolStripButton2", "ToolStripButton", "&Open"),
        ]);
        withItems = withItems.Replace("End Class",
            "Private Sub Shared_Click(sender As Object, e As System.EventArgs) " +
            "Handles ToolStripButton1.Click, ToolStripButton2.Click\nEnd Sub\nEnd Class", StringComparison.Ordinal);

        var removal = new WinFormsToolStripDocument(withItems).RemoveItem("ToolStrip1", "ToolStripButton1");

        Assert.Equal(new HashSet<string> { "ToolStripButton1" }, removal.RemovedNames);
        var result = removal.Source;
        Assert.DoesNotContain("ToolStripButton1", result);
        Assert.Contains("ToolStripButton2", result);
        Assert.Contains("Private Sub Shared_Click", result);
        Assert.DoesNotContain("ToolStripButton1.Click", result);
        Assert.Contains("Handles ToolStripButton2.Click", result);
        Assert.Equal([new WinFormsToolStripItem("ToolStripButton2", "ToolStripButton", "&Open")],
            new WinFormsToolStripDocument(result).Read("ToolStrip1"));
    }

    [Fact]
    public void NewToolStripStartsEmpty()
    {
        var document = new WinFormsToolStripDocument(ToolStripSource);
        Assert.Empty(document.Read("ToolStrip1"));
    }

    [Fact]
    public void AddingAButtonThenASeparatorThenAComboBoxRoundTripsThroughSerializedVb()
    {
        var document = new WinFormsToolStripDocument(ToolStripSource);

        var withButton = document.Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "&Save")]);
        var reloaded1 = new WinFormsToolStripDocument(withButton);
        var afterButton = reloaded1.Read("ToolStrip1");
        Assert.Equal([new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "&Save")], afterButton);
        Assert.Contains("Friend WithEvents ToolStripButton1 As Global.System.Windows.Forms.ToolStripButton", withButton);
        Assert.Contains("Me.ToolStrip1.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripButton1})", withButton);

        var withSeparator = reloaded1.Set("ToolStrip1",
            [.. afterButton, new WinFormsToolStripItem("ToolStripSeparator1", "ToolStripSeparator", "")]);
        var reloaded2 = new WinFormsToolStripDocument(withSeparator);
        var afterSeparator = reloaded2.Read("ToolStrip1");
        Assert.Equal(2, afterSeparator.Count);
        Assert.Equal("ToolStripSeparator", afterSeparator[1].TypeName, ignoreCase: true);
        // A separator never gets a Text assignment: nothing to round-trip but its name and type.
        Assert.DoesNotContain("ToolStripSeparator1.Text", withSeparator);

        var withComboBox = reloaded2.Set("ToolStrip1",
            [.. afterSeparator, new WinFormsToolStripItem("ToolStripComboBox1", "ToolStripComboBox", "")]);
        var reloaded3 = new WinFormsToolStripDocument(withComboBox);
        var afterComboBox = reloaded3.Read("ToolStrip1");
        Assert.Equal(3, afterComboBox.Count);
        Assert.Equal("ToolStripComboBox1", afterComboBox[2].Name);
        Assert.Equal("ToolStripComboBox", afterComboBox[2].TypeName);
        // ToolStripComboBox hosts a control instead of painting text; Visual Studio never
        // writes a Text assignment for it, so this document must not invent one either.
        Assert.DoesNotContain("ToolStripComboBox1.Text", withComboBox);
    }

    [Fact]
    public void StatusStripAcceptsStatusLabelAndDropDownButtonKinds()
    {
        var document = new WinFormsToolStripDocument(StatusStripSource);
        var withLabel = document.Set("StatusStrip1",
            [new WinFormsToolStripItem("ToolStripStatusLabel1", "ToolStripStatusLabel", "Ready")]);
        var reloaded = new WinFormsToolStripDocument(withLabel);
        var items = reloaded.Read("StatusStrip1");
        var withDropDown = reloaded.Set("StatusStrip1",
            [.. items, new WinFormsToolStripItem("ToolStripDropDownButton1", "ToolStripDropDownButton", "")]);
        var final = new WinFormsToolStripDocument(withDropDown).Read("StatusStrip1");

        Assert.Equal(2, final.Count);
        Assert.Equal(new WinFormsToolStripItem("ToolStripStatusLabel1", "ToolStripStatusLabel", "Ready"), final[0]);
        Assert.Equal("ToolStripDropDownButton", final[1].TypeName);
    }

    [Fact]
    public void AnItemCannotChangeTypeOnceDeclared()
    {
        var document = new WinFormsToolStripDocument(ToolStripSource);
        var withButton = document.Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "Go")]);
        var reloaded = new WinFormsToolStripDocument(withButton);

        Assert.Throws<ArgumentException>(() => reloaded.Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripLabel", "Go")]));
    }

    [Fact]
    public void NameAlreadyDeclaredElsewhereIsRejected()
    {
        var source = ToolStripSource.Replace("End Class",
            "    Public Property ToolStripButton1 As String\nEnd Class", StringComparison.Ordinal);
        var document = new WinFormsToolStripDocument(source);

        Assert.Throws<ArgumentException>(() => document.Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "Go")]));
    }
}
