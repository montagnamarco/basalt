using System.Resources;
using System.Text;
using System.Xml;

namespace Basalt.Forms.Tests.Resources;

/// <summary>
/// Round-trips a .resx written by hand in exactly the shape Visual Studio
/// emits (full xsd schema, resheaders, an aliased assembly, a plain string
/// with a comment, a file reference, and base64 byte[]) through
/// ResXResourceReader, then writes what was read back out through
/// ResXResourceWriter and reads that too, to prove both ends agree on the
/// format and not just on each other's shortcuts.
/// </summary>
/// <remarks>
/// Every test reads with UseResXDataNodes so GetValue is only ever called on
/// the one entry under test: that mirrors the "resolved only when asked"
/// contract GetValue has, and keeps an unrelated entry's file reference from
/// failing a test that has nothing to do with it.
/// </remarks>
public class ResXRoundTripTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-resx-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A real-looking Visual Studio .resx: the full schema block, the four standard resheaders, an &lt;assembly&gt; alias, and one data element each for a commented string, a byte[] and a file reference.</summary>
    private const string VisualStudioStyleResx = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
            <xsd:element name="root" msdata:IsDataSet="true">
              <xsd:complexType>
                <xsd:choice maxOccurs="unbounded">
                  <xsd:element name="data">
                    <xsd:complexType>
                      <xsd:sequence>
                        <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                        <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
                      </xsd:sequence>
                      <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
                      <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
                      <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
                    </xsd:complexType>
                  </xsd:element>
                </xsd:choice>
              </xsd:complexType>
            </xsd:element>
          </xsd:schema>
          <resheader name="resmimetype">
            <value>text/microsoft-resx</value>
          </resheader>
          <resheader name="version">
            <value>2.0</value>
          </resheader>
          <resheader name="reader">
            <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
          <resheader name="writer">
            <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
          <assembly alias="mscorlib" name="mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"/>
          <data name="Greeting" xml:space="preserve">
            <value>Hello, Visual Studio</value>
            <comment>the classic greeting string</comment>
          </data>
          <data name="RawBytes" type="System.Byte[], mscorlib">
            <value>AQIDBAU=</value>
          </data>
          <data name="LicenseFile" type="System.Resources.ResXFileRef, System.Windows.Forms">
            <value>license.txt;System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089;utf-8</value>
          </data>
        </root>
        """;

    [Fact]
    public void Reads_a_plain_string_with_its_comment()
    {
        using var reader = ResXResourceReader.FromFileContents(VisualStudioStyleResx);
        reader.UseResXDataNodes = true;

        var node = Node(reader, "Greeting");

        Assert.Equal("Hello, Visual Studio", node.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
        Assert.Equal("the classic greeting string", node.Comment);
    }

    [Fact]
    public void Reads_a_base64_byte_array_through_the_mscorlib_alias()
    {
        using var reader = ResXResourceReader.FromFileContents(VisualStudioStyleResx);
        reader.UseResXDataNodes = true;

        var node = Node(reader, "RawBytes");

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, node.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
    }

    [Fact]
    public void Reads_a_file_reference_by_loading_the_referenced_file()
    {
        File.WriteAllText(Path.Combine(_directory, "license.txt"), "MIT", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        using var reader = new ResXResourceReader(WriteToFile(VisualStudioStyleResx)) { BasePath = _directory, UseResXDataNodes = true };

        var node = Node(reader, "LicenseFile");

        Assert.Equal("MIT", node.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
        Assert.NotNull(node.FileRef);
    }

    [Fact]
    public void Resolving_without_UseResXDataNodes_returns_the_value_directly()
    {
        using var reader = ResXResourceReader.FromFileContents(VisualStudioStyleResx);

        Assert.Equal("Hello, Visual Studio", Value(reader, "Greeting"));
    }

    [Fact]
    public void Written_resources_read_back_with_the_same_values_and_comment()
    {
        var path = Path.Combine(_directory, "roundtrip.resx");
        var referencedFile = Path.Combine(_directory, "data.bin");
        File.WriteAllBytes(referencedFile, [9, 8, 7]);

        using (var writer = new ResXResourceWriter(path))
        {
            writer.AddResource(new ResXDataNode("Greeting", "Hello, Visual Studio") { Comment = "the classic greeting string" });
            writer.AddResource("RawBytes", new byte[] { 1, 2, 3, 4, 5 });
            writer.AddResource("FileRef", new ResXFileRef(referencedFile, typeof(byte[]).AssemblyQualifiedName!));
            writer.AddMetadata("DesignerOnly", "not a real resource");
        }

        using var reader = new ResXResourceReader(path);
        reader.UseResXDataNodes = true;

        var greeting = Node(reader, "Greeting");
        Assert.Equal("Hello, Visual Studio", greeting.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
        Assert.Equal("the classic greeting string", greeting.Comment);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, Node(reader, "RawBytes").GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
        Assert.Equal(new byte[] { 9, 8, 7 }, Node(reader, "FileRef").GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));

        using var reopened = new ResXResourceReader(path) { UseResXDataNodes = true };
        var metadataNode = MetadataNode(reopened, "DesignerOnly");
        Assert.Equal("not a real resource", metadataNode.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
    }

    [Fact]
    public void A_binary_formatter_mimetype_parses_but_throws_only_when_the_value_is_requested()
    {
        const string resxWithLegacyBinaryPayload = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Legacy" type="System.Object, mscorlib" mimetype="application/x-microsoft.net.object.binary.base64">
                <value>AAEAAAD/////</value>
              </data>
            </root>
            """;

        using var reader = ResXResourceReader.FromFileContents(resxWithLegacyBinaryPayload);
        reader.UseResXDataNodes = true;

        // Parsing the document and finding the node must not throw by itself.
        var node = Node(reader, "Legacy");
        Assert.Equal("Legacy", node.Name);

        var exception = Assert.Throws<NotSupportedException>(() => node.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null));
        Assert.Contains("BinaryFormatter", exception.Message);
    }

    [Fact]
    public void Generate_cannot_be_called_twice()
    {
        using var writer = new ResXResourceWriter(new MemoryStream());
        writer.AddResource("A", "value");
        writer.Generate();

        Assert.Throws<InvalidOperationException>(writer.Generate);
    }

    private string WriteToFile(string contents)
    {
        var path = Path.Combine(_directory, "input.resx");
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Requires UseResXDataNodes; stops at the matching entry, so an unrelated entry's value is never resolved.</summary>
    [Fact]
    public void A_resx_with_a_document_type_definition_is_refused()
    {
        const string withEntity = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE root [ <!ENTITY secret SYSTEM "file:///etc/hosts"> ]>
            <root>
              <data name="Leak"><value>&secret;</value></data>
            </root>
            """;
        using var reader = new ResXResourceReader(new StringReader(withEntity.Trim()));

        Assert.Throws<XmlException>(() => reader.GetEnumerator());
    }

    private static ResXDataNode Node(ResXResourceReader reader, string name) => (ResXDataNode)Find(reader.GetEnumerator(), name)!;

    private static ResXDataNode MetadataNode(ResXResourceReader reader, string name) => (ResXDataNode)Find(reader.GetMetadataEnumerator(), name)!;

    /// <summary>Resolves only the named entry's value (UseResXDataNodes off).</summary>
    private static object? Value(ResXResourceReader reader, string name) => Find(reader.GetEnumerator(), name);

    private static object? Find(System.Collections.IDictionaryEnumerator enumerator, string name)
    {
        while (enumerator.MoveNext())
        {
            if ((string)enumerator.Key == name) return enumerator.Value;
        }

        throw new KeyNotFoundException(name);
    }
}
