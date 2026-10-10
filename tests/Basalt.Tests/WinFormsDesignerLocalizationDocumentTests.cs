using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Designer.vb writes for the Localizable property. The fixtures below are written by hand
/// the way Visual Studio writes a VB form: the ComponentResourceManager declaration first,
/// a comment header per control, ApplyResources(Me.Control, "Control") in the control's
/// section and ApplyResources(Me, "$this") after the AutoScale statements. None of them were
/// produced by Basalt, so a test that passes against them proves the layout, not the writer's
/// own output.
/// </summary>
/// <remarks>
/// Defect reintroduced once to prove the round trip tests catch it: each of the writer's line
/// placements was changed in turn, and the expected Designer.vb text stopped matching.
/// </remarks>
public sealed class WinFormsDesignerLocalizationDocumentTests
{
    /// <summary>Localizable = False, as Visual Studio writes a form with no localized values yet.</summary>
    internal static readonly string NotLocalized = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.SuspendLayout()
                '
                'Button1
                '
                Me.Button1.Location = New System.Drawing.Point(40, 50)
                Me.Button1.Name = "Button1"
                Me.Button1.Size = New System.Drawing.Size(120, 35)
                Me.Button1.Text = "Button"
                Me.Button1.UseVisualStyleBackColor = True
                '
                'MainForm
                '
                Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
                Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.Button1)
                Me.Name = "MainForm"
                Me.Text = "Main form"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>Localizable = True, as Visual Studio writes it after the values moved to MainForm.resx.</summary>
    internal static readonly string Localized = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                Me.Button1 = New System.Windows.Forms.Button()
                Me.SuspendLayout()
                '
                'Button1
                '
                resources.ApplyResources(Me.Button1, "Button1")
                Me.Button1.Name = "Button1"
                Me.Button1.UseVisualStyleBackColor = True
                '
                'MainForm
                '
                Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
                Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
                resources.ApplyResources(Me, "$this")
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.Button1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>The values Localizable = True moves out of <see cref="NotLocalized"/>.</summary>
    private static readonly (string ControlName, string PropertyName)[] MovedProperties =
    [
        ("Button1", "Location"),
        ("Button1", "Size"),
        ("Button1", "Text"),
        ("Me", "Text"),
    ];

    /// <summary>The values Localizable = False puts back, in the expression form the Shell writes.</summary>
    private static readonly WinFormsLocalizedAssignment[] RestoredAssignments =
    [
        new("Button1", "Location", "New System.Drawing.Point(40, 50)"),
        new("Button1", "Size", "New System.Drawing.Size(120, 35)"),
        new("Button1", "Text", "\"Button\""),
        new("Me", "Text", "\"Main form\""),
    ];

    [Fact]
    public void IsLocalizableReadsTheApplyResourcesCallOnTheForm()
    {
        Assert.True(new WinFormsDesignerDocument(Localized).IsLocalizable);
        Assert.False(new WinFormsDesignerDocument(NotLocalized).IsLocalizable);
    }

    [Fact]
    public void EnableLocalizationDeclaresTheManagerAndMovesTheValuesOutOfCode()
    {
        // Expected text: the Visual Studio layout of the same form with Localizable = True.
        var written = new WinFormsDesignerDocument(NotLocalized).EnableLocalization(MovedProperties);

        Assert.Equal(Localized, written);
    }

    [Fact]
    public void DisableLocalizationPutsTheValuesBackAndRemovesTheUnusedManager()
    {
        var written = new WinFormsDesignerDocument(Localized).DisableLocalization(RestoredAssignments);

        Assert.Equal(DisabledFromLocalized, written);
    }

    [Fact]
    public void LocalizedFormRoundTripsByteForByte()
    {
        // Reading and saving without a change keeps the file identical.
        Assert.Equal(Localized, new WinFormsDesignerDocument(Localized).Source);

        // Disabling and enabling again with the same values returns the original file.
        var disabled = new WinFormsDesignerDocument(Localized).DisableLocalization(RestoredAssignments);
        var enabled = new WinFormsDesignerDocument(disabled).EnableLocalization(MovedProperties);
        Assert.Equal(Localized, enabled);
    }

    [Fact]
    public void EnablingAnAlreadyLocalizedFormWritesNothingNew()
    {
        // The ApplyResources calls exist already, so a second enable must not add them again.
        var written = new WinFormsDesignerDocument(Localized).EnableLocalization([]);

        Assert.Equal(Localized, written);
    }

    [Fact]
    public void DisableKeepsTheManagerWhileAnotherStatementStillReadsIt()
    {
        // An image is resource-backed by a GetObject call on the same manager, so the declaration stays.
        var source = Localized.Replace("        Me.Button1.Name = \"Button1\"\n",
            "        Me.Button1.Name = \"Button1\"\n"
            + "        Me.Button1.Image = CType(resources.GetObject(\"Button1.Image\"), System.Drawing.Image)\n",
            StringComparison.Ordinal);

        var written = new WinFormsDesignerDocument(source).DisableLocalization(
            [new WinFormsLocalizedAssignment("Me", "Text", "\"Main form\"")]);

        var reparsed = new WinFormsDesignerDocument(written);
        Assert.Contains("Dim resources As System.ComponentModel.ComponentResourceManager", written);
        Assert.DoesNotContain("ApplyResources(Me.Button1", written);
        Assert.Single(reparsed.ResourceOperations, operation => operation.ResourceKey == "Button1.Image");
    }

    /// <remarks>
    /// Defect reintroduced once (LineSpan returned only the first line of a statement): the continuation line of the Text
    /// stayed in InitializeComponent, and the exact-text assertion failed with the stray line in the output.
    /// </remarks>
    [Fact]
    public void EnableLocalizationRemovesAWholeContinuedTextStatement()
    {
        // The VB writer continues a long Text with " & _" and a line break, as Visual Studio does.
        var source = NotLocalized.ReplaceLineEndings("\n").Replace("        Me.Button1.Text = \"Button\"\n",
            "        Me.Button1.Text = \"Button and \" & _\n            \"more text\"\n", StringComparison.Ordinal);
        // The pattern must have matched, or the test would prove nothing about the continuation.
        Assert.Contains("more text", source, StringComparison.Ordinal);

        var written = new WinFormsDesignerDocument(source).EnableLocalization(MovedProperties);

        Assert.Equal(Localized, written);
    }

    /// <summary>Expected Designer.vb after Localizable = False from <see cref="Localized"/>.</summary>
    internal static readonly string DisabledFromLocalized = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.SuspendLayout()
                '
                'Button1
                '
                Me.Button1.Location = New System.Drawing.Point(40, 50)
                Me.Button1.Size = New System.Drawing.Size(120, 35)
                Me.Button1.Text = "Button"
                Me.Button1.Name = "Button1"
                Me.Button1.UseVisualStyleBackColor = True
                '
                'MainForm
                '
                Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
                Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
                Me.Text = "Main form"
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.Button1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """.ReplaceLineEndings("\n");
}
