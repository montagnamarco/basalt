using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Covers the root cause of "cannot set Image": Image/Icon properties were
/// classified read-only with no editor, and nothing could write the resx entry
/// or the InitializeComponent line Visual Studio uses for a resource-backed
/// picture. These tests exercise the pure Basalt.Workspace/Basalt.Designer
/// layer without Avalonia.
/// </summary>
public sealed class WinFormsImageResourcesTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public void EncodeThenDecodeRoundTripsBytesWithVisualStudioTypeAndMimetype()
    {
        var (value, typeName, mimeType) = WinFormsImageResources.Encode(PngBytes, isIcon: false);
        Assert.Equal("System.Drawing.Bitmap, System.Drawing", typeName);
        Assert.Equal("application/x-microsoft.net.object.bytearray.base64", mimeType);

        var entry = new WinFormsResourceEntry("Button1.Image", typeName, mimeType, value, null);
        Assert.True(WinFormsImageResources.TryDecode(entry, out var decoded));
        Assert.Equal(PngBytes, decoded);
    }

    [Fact]
    public void DecodeAcceptsVisualStudiosOwnMultilineIndentedBase64()
    {
        // Visual Studio wraps the base64 value across several indented lines;
        // the designer must read a real .resx it wrote without re-exporting it.
        var value = Convert.ToBase64String(PngBytes);
        var wrapped = "\n        " + value[..(value.Length / 2)] + "\n        " + value[(value.Length / 2)..] + "\n    ";
        var entry = new WinFormsResourceEntry("$this.Icon", "System.Drawing.Icon, System.Drawing",
            "application/x-microsoft.net.object.bytearray.base64", wrapped, null);
        Assert.True(WinFormsImageResources.TryDecode(entry, out var decoded));
        Assert.Equal(PngBytes, decoded);
    }

    [Fact]
    public void DecodeRejectsEntriesWithoutTheByteArrayMimetypeOrAnUnrelatedType()
    {
        var binaryFormatter = new WinFormsResourceEntry("Bitmap1", null,
            "application/x-microsoft.net.object.binary.base64", Convert.ToBase64String(PngBytes), null);
        Assert.False(WinFormsImageResources.TryDecode(binaryFormatter, out _));

        var fileRef = new WinFormsResourceEntry("Image1", "System.Resources.ResXFileRef, System.Windows.Forms",
            "application/x-microsoft.net.object.bytearray.base64", "secret.png;System.Drawing.Bitmap", null);
        Assert.False(WinFormsImageResources.TryDecode(fileRef, out _));
    }

    [Fact]
    public void SetImageResourceDeclaresComponentResourceManagerOnceAndWritesTheGetObjectAssignment()
    {
        var document = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);

        var afterFirst = document.SetImageResource("Button1", "Image", "Button1.Image", "System.Drawing.Image");
        Assert.Contains(
            "Dim resources As System.ComponentModel.ComponentResourceManager = " +
            "New System.ComponentModel.ComponentResourceManager(GetType(MainForm))", afterFirst);
        Assert.Contains("Me.Button1.Image = CType(resources.GetObject(\"Button1.Image\"), System.Drawing.Image)", afterFirst);

        // A second image property reuses the manager already declared; Visual
        // Studio only ever writes one "Dim resources" line per InitializeComponent.
        var secondDocument = new WinFormsDesignerDocument(afterFirst);
        var afterSecond = secondDocument.SetImageResource("Me", "Icon", "$this.Icon", "System.Drawing.Icon");
        var managerCount = afterSecond.Split("ComponentResourceManager(GetType(MainForm))").Length - 1;
        Assert.Equal(1, managerCount);
        Assert.Contains("Me.Icon = CType(resources.GetObject(\"$this.Icon\"), System.Drawing.Icon)", afterSecond);
    }

    [Fact]
    public void RemoveImageResourceDeletesOnlyTheAssignmentStatement()
    {
        var document = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);
        var withImage = new WinFormsDesignerDocument(
            document.SetImageResource("Button1", "Image", "Button1.Image", "System.Drawing.Image"));

        var cleared = withImage.RemoveImageResource("Button1", "Image");
        Assert.DoesNotContain("Button1.Image", cleared);
        Assert.Contains("Me.Button1.Text = \"Before\"", cleared);
    }

    [Fact]
    public void DesignerPropertyInspectorStopsTreatingImageAndIconAsReadOnly()
    {
        // This is the defect the user reported: Image/Icon rows showed up read-only
        // with no editor, so the Properties grid had no way to set them at all.
        var properties = WinFormsPropertyInspector.DescribeDesigner(new System.Windows.Forms.Button());
        var image = Assert.Single(properties, property => property.Name == "Image");
        Assert.False(image.IsReadOnly);
        Assert.True(image.HasCustomEditor);
    }

    [Fact]
    public void EmptyTemplateMatchesTheVisualStudioHeaderAndParsesWithNoEntries()
    {
        // Copied from a real Visual Studio-authored .resx fixture; a freshly
        // created resource document must parse like any other and start empty.
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>", WinFormsResourceDocumentTemplate.Empty);
        Assert.EndsWith("</root>", WinFormsResourceDocumentTemplate.Empty);
        Assert.Contains("Microsoft ResX Schema", WinFormsResourceDocumentTemplate.Empty);

        var document = new WinFormsResourceDocument(WinFormsResourceDocumentTemplate.Empty);
        Assert.Empty(document.Entries);

        // The template must still accept a new entry the same way a hand-written resx does.
        var withEntry = new WinFormsResourceDocument(document.SetValue("Button1.Image", "AAAA",
            "System.Drawing.Bitmap, System.Drawing", "application/x-microsoft.net.object.bytearray.base64"));
        Assert.Equal("AAAA", withEntry.TryGet("Button1.Image")!.Value);
    }

    [Fact]
    public async Task SettingAnImageCreatesTheResxWhenTheSessionHasNoneYet()
    {
        // The root cause of "cannot add an Image to a fresh form": a form without
        // a .resx yet had no way to get one, so the very first picture property
        // always threw WinFormsDesignerCodeUnavailable.
        using var withoutCreation = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var button = withoutCreation.FindControl("Button1")!;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            withoutCreation.SetImageResourcePropertyAsync(button, "Image", PngBytes, isIcon: false));
        Assert.Equal(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable), thrown.Message);

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var created = 0;
        session.CreateResourceDocument = () =>
        {
            ++created;
            return Task.FromResult(WinFormsResourceDocumentTemplate.Empty);
        };
        var target = session.FindControl("Button1")!;
        await session.SetImageResourcePropertyAsync(target, "Image", PngBytes, isIcon: false);
        Assert.Equal(1, created);
        Assert.Contains("Me.Button1.Image = CType(resources.GetObject(\"Button1.Image\"), System.Drawing.Image)",
            session.Document.Source);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.True(WinFormsImageResources.TryDecode(resources.TryGet("Button1.Image")!, out var decoded));
        Assert.Equal(PngBytes, decoded);

        // Undo must revert the assignment and the resx entry together, without
        // forgetting the file (and its header) that creation just produced.
        await session.UndoAsync();
        Assert.DoesNotContain("Button1.Image", session.Document.Source);
        Assert.Empty(new WinFormsResourceDocument(session.ResourceSource!).Entries);
        Assert.Equal(1, created);
    }
}
