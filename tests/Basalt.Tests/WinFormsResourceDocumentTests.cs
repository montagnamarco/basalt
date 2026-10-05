using System.Xml;
using System.Xml.Linq;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsResourceDocumentTests
{
    private const string Source = """
        <?xml version="1.0" encoding="utf-8"?>
        <root xmlns:custom="urn:custom">
          <!-- Keep the original formatting. -->
          <assembly alias='drawing' name='System.Drawing, Version=4.0.0.0' />
          <data name='Button1.Text' xml:space='preserve'><value>Hello &amp; world</value><comment>Translator note</comment><custom:extra mode='keep' /></data>
          <data name="Button1.Size" type="System.Drawing.Size, drawing"><value>120, 32</value></data>
          <data name='Unknown' type='Application.Dangerous, App' mimetype='application/x-custom'><value>DO NOT EXECUTE</value></data>
          <metadata name='Designer.Metadata'><value>Leave metadata alone</value></metadata>
        </root>
        """;

    [Fact]
    public void ReadsRawTypesMimeCommentsAndValuesWithoutResolvingAnything()
    {
        var document = new WinFormsResourceDocument(Source);
        Assert.Equal(Source, document.Source);
        Assert.Equal(3, document.Entries.Count);
        Assert.Equal("Hello & world", document.TryGet("Button1.Text")!.Value);
        Assert.Equal("Translator note", document.TryGet("Button1.Text")!.Comment);
        Assert.Equal("System.Drawing.Size, drawing", document.TryGet("Button1.Size")!.TypeName);
        Assert.Equal("Application.Dangerous, App", document.TryGet("Unknown")!.TypeName);
        Assert.Equal("application/x-custom", document.TryGet("Unknown")!.MimeType);
        Assert.Null(document.TryGet("Designer.Metadata"));
        Assert.Null(document.TryGet("Missing"));
    }

    [Fact]
    public void EditingOneEntryPreservesEveryUnrelatedByteAndItsComment()
    {
        var document = new WinFormsResourceDocument(Source);
        var changed = document.SetString("Button1.Text", "  <Changed> & quoted \"text\"  ");
        var originalStart = Source.IndexOf("<data name='Button1.Text'", StringComparison.Ordinal);
        var originalEnd = Source.IndexOf("</data>", originalStart, StringComparison.Ordinal) + "</data>".Length;
        Assert.StartsWith(Source[..originalStart], changed);
        Assert.EndsWith(Source[originalEnd..], changed);
        var reopened = new WinFormsResourceDocument(changed);
        Assert.Equal("  <Changed> & quoted \"text\"  ", reopened.TryGet("Button1.Text")!.Value);
        Assert.Equal("Translator note", reopened.TryGet("Button1.Text")!.Comment);
        var entry = XDocument.Parse(changed).Root!.Elements("data").First();
        Assert.NotNull(entry.Element(XName.Get("extra", "urn:custom")));
        Assert.Equal(changed, reopened.SetString("Button1.Text", reopened.TryGet("Button1.Text")!.Value));
    }

    [Fact]
    public void CommentEditsKeepOpaqueAttributesAndUnrelatedResourceBytes()
    {
        var document = new WinFormsResourceDocument(Source);
        var changedScalar = document.SetValue("Button1.Text", "Hello & world", null,
            null, "Updated translator note");
        var scalar = new WinFormsResourceDocument(changedScalar).TryGet("Button1.Text")!;
        Assert.Equal("Updated translator note", scalar.Comment);
        Assert.NotNull(XDocument.Parse(changedScalar).Root!.Elements("data").First()
            .Element(XName.Get("extra", "urn:custom")));
        Assert.EndsWith(Source[Source.IndexOf("<data name=\"Button1.Size\"", StringComparison.Ordinal)..],
            changedScalar);

        var changed = document.SetValue("Unknown", "DO NOT EXECUTE", "Application.Dangerous, App",
            "application/x-custom", "Safe note & <detail>");

        Assert.StartsWith(Source[..Source.IndexOf("<data name='Unknown'", StringComparison.Ordinal)], changed);
        Assert.Contains("<metadata name='Designer.Metadata'><value>Leave metadata alone</value></metadata>", changed);
        var reopened = new WinFormsResourceDocument(changed);
        var opaque = reopened.TryGet("Unknown")!;
        Assert.Equal("Application.Dangerous, App", opaque.TypeName);
        Assert.Equal("application/x-custom", opaque.MimeType);
        Assert.Equal("DO NOT EXECUTE", opaque.Value);
        Assert.Equal("Safe note & <detail>", opaque.Comment);
        Assert.Equal(changed, reopened.SetValue("Unknown", opaque.Value, opaque.TypeName,
            opaque.MimeType, opaque.Comment));

        var removedComment = new WinFormsResourceDocument(reopened.SetValue("Unknown", opaque.Value,
            opaque.TypeName, opaque.MimeType, null));
        Assert.Null(removedComment.TryGet("Unknown")!.Comment);
    }

    [Fact]
    public void CommentOnlyEditRetainsOpaqueCdataAndNestedValueComments()
    {
        const string source = "<root><data name='Opaque' type='Custom.Type, User' mimetype='application/x-custom'>"
            + "<value><![CDATA[encoded]]><!-- inside value -->tail</value><comment>Before</comment></data></root>";
        var resource = new WinFormsResourceDocument(source);
        var entry = resource.TryGet("Opaque")!;
        var changed = resource.SetValue(entry.Name, entry.Value, entry.TypeName,
            entry.MimeType, "After");

        Assert.Contains("<value><![CDATA[encoded]]><!-- inside value -->tail</value>", changed);
        Assert.Equal("After", new WinFormsResourceDocument(changed).TryGet("Opaque")!.Comment);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void AddsRemovesAndUpdatesRawEntriesWithXmlLineOffsets(string newline)
    {
        var source = "<root>" + newline + "  <data name='First'><value>A\u2028B\u0085C</value></data>" + newline
            + "  <data name='Empty' marker='greater&gt;than' />" + newline + "</root>";
        var document = new WinFormsResourceDocument(source);
        var changed = document.SetValue("Empty", "False", "System.Boolean, mscorlib");
        var reopened = new WinFormsResourceDocument(changed);
        Assert.Equal("False", reopened.TryGet("Empty")!.Value);
        Assert.Equal("A\u2028B\u0085C", reopened.TryGet("First")!.Value);
        var added = new WinFormsResourceDocument(reopened.SetValue("Added", "raw", "Custom.Type", "custom/mime"));
        Assert.Equal("custom/mime", added.TryGet("Added")!.MimeType);
        Assert.Equal(changed, reopened.Remove("Missing"));
        var removed = new WinFormsResourceDocument(added.Remove("First"));
        Assert.Null(removed.TryGet("First"));
        Assert.NotNull(removed.TryGet("Empty"));
        Assert.NotNull(removed.TryGet("Added"));
    }

    [Fact]
    public void EmptyRootCanReceiveAnEntryWithoutChangingSurroundingXml()
    {
        const string source = "<?xml version='1.0'?>\n<!-- keep -->\n<root marker='keep' />\n<!-- trailing -->";
        var document = new WinFormsResourceDocument(source);
        Assert.Empty(document.Entries);
        var changed = document.SetString("Added", "Value");
        Assert.StartsWith("<?xml version='1.0'?>\n<!-- keep -->\n", changed);
        Assert.EndsWith("\n<!-- trailing -->", changed);
        Assert.Equal("keep", XDocument.Parse(changed).Root!.Attribute("marker")!.Value);
        Assert.Equal("Value", new WinFormsResourceDocument(changed).TryGet("Added")!.Value);
    }

    [Fact]
    public void RejectsDtdAndDuplicateKeysWithoutLoadingExternalResources()
    {
        Assert.Throws<XmlException>(() => new WinFormsResourceDocument(
            "<!DOCTYPE root [<!ENTITY injected SYSTEM 'file:///must-not-read'>]><root><data name='Key'><value>&injected;</value></data></root>"));
        Assert.Throws<FormatException>(() => new WinFormsResourceDocument(
            "<root><data name='Key'><value>A</value></data><data name='Key'><value>B</value></data></root>"));
    }
}
