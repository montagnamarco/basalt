using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace System.Resources;

/// <summary>
/// Writes a .resx file in the shape Visual Studio reads: a &lt;resheader&gt;
/// block naming the format version and the reader/writer types, then one
/// &lt;data&gt; element per resource and one &lt;metadata&gt; per
/// designer-only value, each carrying a type attribute only when the value
/// is not a plain string.
/// </summary>
/// <remarks>
/// AddResource/AddMetadata only buffer; nothing reaches the stream until
/// Generate (or Dispose/Close, which call it) runs - the same two-phase
/// shape Microsoft's writer has, so a caller can add resources in any order
/// and have them land in the file in the order they were added.
/// Values are written as a string (via their TypeConverter), a file
/// reference, or base64 for a byte[]; a value whose TypeConverter cannot
/// round-trip through a string has no BinaryFormatter fallback here (that
/// serializer is obsolete and unsafe), so AddResource throws for it instead
/// of silently producing a file nothing can read back.
/// </remarks>
public class ResXResourceWriter : IDisposable, IResourceWriter
{
    public static readonly string BinSerializedObjectMimeType = "application/x-microsoft.net.object.binary.base64";
    public static readonly string ByteArraySerializedObjectMimeType = "application/x-microsoft.net.object.bytearray.base64";
    public static readonly string DefaultSerializedObjectMimeType = BinSerializedObjectMimeType;
    public static readonly string ResMimeType = "text/microsoft-resx";
    public static readonly string SoapSerializedObjectMimeType = "application/x-microsoft.net.object.soap.base64";
    public static readonly string Version = "2.0";

    /// <summary>The xsd Visual Studio embeds in every .resx so a schema-aware editor can validate it; we do not validate against it ourselves.</summary>
    public static readonly string ResourceSchema = """
        <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
          <xsd:element name="root" msdata:IsDataSet="true">
            <xsd:complexType>
              <xsd:choice maxOccurs="unbounded">
                <xsd:element name="metadata">
                  <xsd:complexType>
                    <xsd:sequence>
                      <xsd:element name="value" type="xsd:string" minOccurs="0" />
                    </xsd:sequence>
                    <xsd:attribute name="name" use="required" type="xsd:string" />
                    <xsd:attribute name="type" type="xsd:string" />
                    <xsd:attribute name="mimetype" type="xsd:string" />
                  </xsd:complexType>
                </xsd:element>
                <xsd:element name="assembly">
                  <xsd:complexType>
                    <xsd:attribute name="alias" type="xsd:string" />
                    <xsd:attribute name="name" type="xsd:string" />
                  </xsd:complexType>
                </xsd:element>
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
                <xsd:element name="resheader">
                  <xsd:complexType>
                    <xsd:sequence>
                      <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                    </xsd:sequence>
                    <xsd:attribute name="name" type="xsd:string" use="required" />
                  </xsd:complexType>
                </xsd:element>
              </xsd:choice>
            </xsd:complexType>
          </xsd:element>
        </xsd:schema>
        """;

    private readonly TextWriter? _externalWriter;
    private readonly Stream? _externalStream;
    private readonly string? _fileName;
    private readonly Func<Type, string>? _typeNameConverter;

    private readonly List<ResXDataNode> _resources = [];
    private readonly List<ResXDataNode> _metadata = [];
    private readonly Dictionary<string, AssemblyName> _aliases = new(StringComparer.Ordinal);

    private bool _generated;
    private bool _disposed;

    public ResXResourceWriter(Stream stream) => _externalStream = stream;

    public ResXResourceWriter(Stream stream, Func<Type, string> typeNameConverter) : this(stream) => _typeNameConverter = typeNameConverter;

    public ResXResourceWriter(TextWriter textWriter) => _externalWriter = textWriter;

    public ResXResourceWriter(TextWriter textWriter, Func<Type, string> typeNameConverter) : this(textWriter) => _typeNameConverter = typeNameConverter;

    public ResXResourceWriter(string fileName) => _fileName = fileName;

    public ResXResourceWriter(string fileName, Func<Type, string> typeNameConverter) : this(fileName) => _typeNameConverter = typeNameConverter;

    public string? BasePath { get; set; }

    public virtual void AddAlias(string aliasName, AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(aliasName);
        ArgumentNullException.ThrowIfNull(assemblyName);

        _aliases[aliasName] = assemblyName;
    }

    public void AddMetadata(string name, byte[] value) => _metadata.Add(MakeNode(name, value));

    public void AddMetadata(string name, object? value) => _metadata.Add(MakeNode(name, value));

    public void AddMetadata(string name, string value) => _metadata.Add(MakeNode(name, value));

    public void AddResource(ResXDataNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Replace(_resources, node);
    }

    public void AddResource(string name, byte[]? value) => Replace(_resources, MakeNode(name, value));

    public void AddResource(string name, object? value) => Replace(_resources, MakeNode(name, value));

    public void AddResource(string name, string? value) => Replace(_resources, MakeNode(name, value));

    private ResXDataNode MakeNode(string name, object? value) =>
        value is ResXFileRef fileRef ? new ResXDataNode(name, fileRef, _typeNameConverter) : new ResXDataNode(name, value, _typeNameConverter);

    /// <summary>AddResource/AddMetadata replace an earlier entry with the same name in place, the way a Hashtable-backed writer would, but keep the list's order stable for a deterministic file.</summary>
    private static void Replace(List<ResXDataNode> nodes, ResXDataNode node)
    {
        var existing = nodes.FindIndex(n => n.Name == node.Name);
        if (existing >= 0) nodes[existing] = node;
        else nodes.Add(node);
    }

    public void Close() => Dispose();

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (!_generated) Generate();

            if (_fileName is not null) _ownedWriter?.Dispose();
        }

        _disposed = true;
    }

    ~ResXResourceWriter()
    {
        Dispose(false);
    }

    private TextWriter? _ownedWriter;

    /// <summary>Writes every buffered resource and metadata entry to the underlying stream/file/writer; only once - a second call is a bug in the caller, so it throws rather than silently writing nothing.</summary>
    public void Generate()
    {
        if (_generated) throw new InvalidOperationException("ResXResourceWriter.Generate was already called: a writer serializes its output exactly once.");
        _generated = true;

        var writer = ResolveWriter();

        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = Encoding.UTF8 };
        using var xmlWriter = XmlWriter.Create(writer, settings);

        xmlWriter.WriteStartElement("root");

        // The xsd is informational only; our own reader does not validate against it.
        xmlWriter.WriteRaw("\n  " + ResourceSchema.Replace("\n", "\n  "));

        WriteResHeader(xmlWriter, "resmimetype", ResMimeType);
        WriteResHeader(xmlWriter, "version", Version);
        WriteResHeader(xmlWriter, "reader", typeof(ResXResourceReader).AssemblyQualifiedName!);
        WriteResHeader(xmlWriter, "writer", typeof(ResXResourceWriter).AssemblyQualifiedName!);

        foreach (var (alias, assemblyName) in _aliases)
        {
            xmlWriter.WriteStartElement("assembly");
            xmlWriter.WriteAttributeString("alias", alias);
            xmlWriter.WriteAttributeString("name", assemblyName.FullName);
            xmlWriter.WriteEndElement();
        }

        foreach (var node in _resources) WriteNode(xmlWriter, "data", node);
        foreach (var node in _metadata) WriteNode(xmlWriter, "metadata", node);

        xmlWriter.WriteEndElement();
        xmlWriter.Flush();

        if (_fileName is not null) writer.Flush();
    }

    private static void WriteResHeader(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement("resheader");
        writer.WriteAttributeString("name", name);
        writer.WriteElementString("value", value);
        writer.WriteEndElement();
    }

    private void WriteNode(XmlWriter writer, string elementName, ResXDataNode node)
    {
        writer.WriteStartElement(elementName);
        writer.WriteAttributeString("name", node.Name);

        var (typeAttribute, valueText) = DescribeValue(node);
        if (typeAttribute is not null) writer.WriteAttributeString("type", typeAttribute);
        else writer.WriteAttributeString("space", XNamespace.Xml.NamespaceName, "preserve");

        writer.WriteElementString("value", valueText);

        if (!string.IsNullOrEmpty(node.Comment)) writer.WriteElementString("comment", node.Comment);

        writer.WriteEndElement();
    }

    /// <summary>The type="..." attribute (null for a plain string) and the text &lt;value&gt; should hold.</summary>
    private (string? TypeAttribute, string ValueText) DescribeValue(ResXDataNode node)
    {
        var value = node.GetValue((System.ComponentModel.Design.ITypeResolutionService?)null);

        switch (value)
        {
            case null:
                return (node.GetValueTypeName((System.ComponentModel.Design.ITypeResolutionService?)null), string.Empty);

            case string text:
                return (null, text);

            case byte[] bytes:
                return ("System.Byte[], mscorlib", Convert.ToBase64String(bytes));

            case ResXFileRef fileRef:
                var relative = MakeRelative(fileRef.FileName);
                var written = relative == fileRef.FileName ? fileRef : CloneWithFileName(fileRef, relative);
                return ($"System.Resources.ResXFileRef, {typeof(ResXFileRef).Assembly.GetName().Name}", written.ToString());

            default:
                var type = value.GetType();
                var converter = TypeDescriptor.GetConverter(type);
                if (!converter.CanConvertTo(typeof(string)) || !converter.CanConvertFrom(typeof(string)))
                {
                    throw new NotSupportedException(
                        $"AddResource('{node.Name}') cannot be written: {type} has no TypeConverter that round-trips through a string, " +
                        "and this writer does not fall back to BinaryFormatter.");
                }

                var typeName = _typeNameConverter?.Invoke(type) ?? type.AssemblyQualifiedName!;
                return (typeName, converter.ConvertToInvariantString(value) ?? string.Empty);
        }
    }

    private static ResXFileRef CloneWithFileName(ResXFileRef fileRef, string fileName) =>
        fileRef.TextFileEncoding is not null ? new ResXFileRef(fileName, fileRef.TypeName, fileRef.TextFileEncoding) : new ResXFileRef(fileName, fileRef.TypeName);

    private string MakeRelative(string fileName)
    {
        if (BasePath is null || !Path.IsPathRooted(fileName)) return fileName;

        try
        {
            return Path.GetRelativePath(BasePath, fileName);
        }
        catch (ArgumentException)
        {
            return fileName;
        }
    }

    private TextWriter ResolveWriter()
    {
        if (_externalWriter is not null) return _externalWriter;
        if (_externalStream is not null) return new StreamWriter(_externalStream, Encoding.UTF8, leaveOpen: true);

        _ownedWriter = new StreamWriter(File.Open(_fileName!, FileMode.Create, FileAccess.Write), Encoding.UTF8);
        return _ownedWriter;
    }
}
