using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Copying controls out of a Localizable form. Each ApplyResources call is resolved from the neutral .resx into literal
/// assignments in the clipboard, so the copy pastes into a form that is not localizable as plain values, and into a
/// Localizable form through the same path a pasted control takes there.
/// </summary>
/// <remarks>
/// Defect reintroduced once to prove these tests catch it: the session copied without resolving the resource values
/// (<c>Document.CopyControls(names, null)</c>), so the clipboard refused the ApplyResources call with NotSupportedException.
/// Both tests failed with that exception.
/// </remarks>
public sealed class WinFormsDesignerLocalizedClipboardTests
{
    private static WinFormsDesignerSession LocalizedSession() => new(
        new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
        new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.NeutralResources));

    [Fact]
    public async Task CopyingFromALocalizableFormPastesLiteralValuesIntoANonLocalizableForm()
    {
        using var source = LocalizedSession();
        var originalSource = source.Document.Source;
        var clipboard = source.CopyControls([source.FindControl("Button1")!]);
        // Copying takes no undo step and leaves the source form as it was.
        Assert.Equal(originalSource, source.Document.Source);
        using var target = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized));

        var added = await target.PasteControlsAsync(clipboard, target.Form);

        Assert.Equal(["Button2"], added);
        Assert.False(target.IsLocalizable);
        // The values are the copied control's own, as the neutral .resx held them for Button1.
        Assert.Equal(new System.Drawing.Point(40, 50), target.FindControl("Button2")!.Location);
        Assert.Equal(new System.Drawing.Size(120, 35), target.FindControl("Button2")!.Size);
        Assert.Equal("Button", target.FindControl("Button2")!.Text);
        Assert.Contains("Me.Button2.Text = ", target.Document.Source);
        Assert.Contains("Me.Button2.Location = ", target.Document.Source);
        Assert.DoesNotContain("ApplyResources", target.Document.Source);
        Assert.DoesNotContain("resources", target.Document.Source);
    }

    [Fact]
    public async Task CopyingAndPastingWithinTheSameLocalizableFormMakesANewControlWithItsOwnResxEntries()
    {
        using var session = LocalizedSession();
        var clipboard = session.CopyControls([session.FindControl("Button1")!]);

        var added = await session.PasteControlsAsync(clipboard, session.Form);

        Assert.Equal(["Button2"], added);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("40, 50", resources.TryGet("Button2.Location")!.Value);
        Assert.Equal("120, 35", resources.TryGet("Button2.Size")!.Value);
        Assert.Equal("Button", resources.TryGet("Button2.Text")!.Value);
        // The copied control keeps its own entries.
        Assert.Equal("Button", resources.TryGet("Button1.Text")!.Value);
        Assert.Contains("        resources.ApplyResources(Me.Button2, \"Button2\")\n", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Location = ", session.Document.Source);
        Assert.Equal("Button", session.FindControl("Button2")!.Text);
        Assert.Equal(new System.Drawing.Point(40, 50), session.FindControl("Button2")!.Location);
    }

    /// <summary>A one-pixel PNG, so a picture decodes: the paste must not depend on the bytes failing to decode.</summary>
    internal const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    /// <summary>A Localizable form whose PictureBox1 keeps its picture as a GetObject read of the neutral .resx.</summary>
    internal static readonly string LocalizedWithPicture = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                Me.PictureBox1 = New System.Windows.Forms.PictureBox()
                Me.SuspendLayout()
                '
                'PictureBox1
                '
                resources.ApplyResources(Me.PictureBox1, "PictureBox1")
                Me.PictureBox1.Name = "PictureBox1"
                Me.PictureBox1.Image = CType(resources.GetObject("PictureBox1.Image"), System.Drawing.Image)
                '
                'MainForm
                '
                resources.ApplyResources(Me, "$this")
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.PictureBox1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents PictureBox1 As System.Windows.Forms.PictureBox
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>The neutral MainForm.resx of <see cref="LocalizedWithPicture"/>: the picture and the values ApplyResources reads.</summary>
    internal static readonly string NeutralWithPicture = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <data name="PictureBox1.Image" type="System.Drawing.Bitmap, System.Drawing" mimetype="application/x-microsoft.net.object.bytearray.base64">
            <value>{OnePixelPng}</value>
          </data>
          <data name="PictureBox1.Location" type="System.Drawing.Point, System.Drawing">
            <value>40, 50</value>
          </data>
          <data name="PictureBox1.Size" type="System.Drawing.Size, System.Drawing">
            <value>120, 35</value>
          </data>
        </root>
        """.ReplaceLineEndings("\n");

    /// <summary>A resx that holds no entries yet, as a non-localizable form's Form.resx starts out.</summary>
    internal const string EmptyResources = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
        </root>
        """;

    /// <remarks>
    /// Defect reintroduced once (the clipboard carried no picture entry and the paste declared no resources local): red
    /// without the fix, failing with Assert.Contains "Sub-string not found" on the resources declaration. Green with it.
    /// </remarks>
    [Fact]
    public async Task CopyingAPictureFromALocalizableFormPastesItsBytesIntoTheTargetsOwnResx()
    {
        using var source = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(LocalizedWithPicture), new WinFormsResourceDocument(NeutralWithPicture));
        var sourcePicture = new WinFormsResourceDocument(NeutralWithPicture).TryGet("PictureBox1.Image")!;
        var clipboard = source.CopyControls([source.FindControl("PictureBox1")!]);
        using var target = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));

        var added = await target.PasteControlsAsync(clipboard, target.Form);

        Assert.Equal(["PictureBox1"], added);
        Assert.False(target.IsLocalizable);
        Assert.Contains("Dim resources As System.ComponentModel.ComponentResourceManager", target.Document.Source);
        Assert.Contains("Me.PictureBox1.Image = CType(resources.GetObject(\"PictureBox1.Image\"), System.Drawing.Image)",
            target.Document.Source);
        var pasted = new WinFormsResourceDocument(target.ResourceSource!).TryGet("PictureBox1.Image")!;
        Assert.Equal(sourcePicture.Value, pasted.Value);
        Assert.Equal(sourcePicture.TypeName, pasted.TypeName);
        Assert.Equal(sourcePicture.MimeType, pasted.MimeType);
        Assert.NotNull(((System.Windows.Forms.PictureBox)target.FindControl("PictureBox1")!).Image);
        Assert.DoesNotContain(target.PreviewProblems, problem => problem.StartsWith("PictureBox1", StringComparison.Ordinal));
        WinFormsCompilationAssert.AssertCompiles(target.Document.Source);
    }

    /// <remarks>
    /// Defect reintroduced once (the pasted read kept the copied key and no entry was written): red without the fix, failing
    /// with Assert.Contains "Sub-string not found" on the pasted PictureBox2 read. Green with it.
    /// </remarks>
    [Fact]
    public async Task CopyingAPictureWithinALocalizableFormGivesTheNewControlItsOwnEntriesAndKeepsItLocalizable()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(LocalizedWithPicture), new WinFormsResourceDocument(NeutralWithPicture));
        var clipboard = session.CopyControls([session.FindControl("PictureBox1")!]);

        var added = await session.PasteControlsAsync(clipboard, session.Form);

        Assert.Equal(["PictureBox2"], added);
        Assert.True(session.IsLocalizable);
        Assert.Contains("Me.PictureBox2.Image = CType(resources.GetObject(\"PictureBox2.Image\"), System.Drawing.Image)",
            session.Document.Source);
        Assert.Contains("resources.ApplyResources(Me.PictureBox2, \"PictureBox2\")", session.Document.Source);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal(OnePixelPng, resources.TryGet("PictureBox1.Image")!.Value);
        Assert.Equal(OnePixelPng, resources.TryGet("PictureBox2.Image")!.Value);
        Assert.Equal("40, 50", resources.TryGet("PictureBox2.Location")!.Value);
        Assert.NotNull(((System.Windows.Forms.PictureBox)session.FindControl("PictureBox2")!).Image);
        WinFormsCompilationAssert.AssertCompiles(session.Document.Source);
    }

    /// <summary>A Localizable form with a ListBox whose neutral .resx holds an entry for the read-only Items property.</summary>
    internal static readonly string LocalizedListBox = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                Me.ListBox1 = New System.Windows.Forms.ListBox()
                Me.SuspendLayout()
                '
                'ListBox1
                '
                resources.ApplyResources(Me.ListBox1, "ListBox1")
                Me.ListBox1.Name = "ListBox1"
                '
                'MainForm
                '
                resources.ApplyResources(Me, "$this")
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.ListBox1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents ListBox1 As System.Windows.Forms.ListBox
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>The neutral MainForm.resx of <see cref="LocalizedListBox"/>: the ListBox's bounds and its Items entry.</summary>
    internal static readonly string NeutralListBox = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <data name="$this.Text" xml:space="preserve">
            <value>Main form</value>
          </data>
          <data name="ListBox1.Items" xml:space="preserve">
            <value>one</value>
          </data>
          <data name="ListBox1.Location" type="System.Drawing.Point, System.Drawing">
            <value>40, 50</value>
          </data>
          <data name="ListBox1.Size" type="System.Drawing.Size, System.Drawing">
            <value>120, 80</value>
          </data>
        </root>
        """.ReplaceLineEndings("\n");

    /// <remarks>
    /// Defect reintroduced once (RestoredAssignments threw on a read-only property's entry): the copy failed with
    /// InvalidOperationException on ListBox1.Items. With the entry skipped but not carried, the paste lost "ListBox2.Items"
    /// and the test failed on that entry.
    /// </remarks>
    [Fact]
    public async Task CopyingAListBoxWhoseItemsEntryIsReadOnlyCarriesTheEntryAndMakesNoLiteralForIt()
    {
        using var source = new WinFormsDesignerSession(new WinFormsDesignerDocument(LocalizedListBox),
            new WinFormsResourceDocument(NeutralListBox));
        var clipboard = source.CopyControls([source.FindControl("ListBox1")!]);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(LocalizedListBox),
            new WinFormsResourceDocument(NeutralListBox));

        var added = await session.PasteControlsAsync(clipboard, session.Form);

        Assert.Equal(["ListBox2"], added);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("one", resources.TryGet("ListBox2.Items")!.Value);
        Assert.Equal("40, 50", resources.TryGet("ListBox2.Location")!.Value);
        Assert.DoesNotContain("ListBox2.Items = ", session.Document.Source);
        Assert.Contains("resources.ApplyResources(Me.ListBox2, \"ListBox2\")", session.Document.Source);
        Assert.DoesNotContain(session.PreviewProblems, problem => problem.StartsWith("ListBox2", StringComparison.Ordinal));
        WinFormsCompilationAssert.AssertCompiles(session.Document.Source);
    }

    [Fact]
    public async Task CopyingAListBoxWhoseItemsEntryIsReadOnlyPastesIntoANonLocalizableFormWithoutFailing()
    {
        using var source = new WinFormsDesignerSession(new WinFormsDesignerDocument(LocalizedListBox),
            new WinFormsResourceDocument(NeutralListBox));
        var clipboard = source.CopyControls([source.FindControl("ListBox1")!]);
        using var target = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));

        var added = await target.PasteControlsAsync(clipboard, target.Form);

        Assert.Equal(["ListBox1"], added);
        Assert.False(target.IsLocalizable);
        Assert.Equal(new System.Drawing.Point(40, 50), target.FindControl("ListBox1")!.Location);
        Assert.DoesNotContain("resources.ApplyResources", target.Document.Source);
        WinFormsCompilationAssert.AssertCompiles(target.Document.Source);
    }
}
