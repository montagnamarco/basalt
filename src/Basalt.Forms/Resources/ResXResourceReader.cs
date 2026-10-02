using System.Collections;
using System.ComponentModel.Design;
using System.Drawing;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace System.Resources;

/// <summary>
/// Reads a .resx file (or stream, or TextReader, or already-loaded text) the
/// way Visual Studio writes one: &lt;resheader&gt; for the format version,
/// &lt;assembly alias="..." name="..."/&gt; for the short names a &lt;data&gt;
/// element's type attribute can use, and &lt;data&gt;/&lt;metadata&gt; for the
/// resources and the designer-only values that travel with them.
/// </summary>
/// <remarks>
/// Parsing is lazy-eager: the whole document is read into ResXDataNode
/// entries the first time anything is enumerated, but each node only turns
/// its stored text into a real object (reading a referenced file, decoding
/// base64, running a TypeConverter) when GetValue is asked for it - see
/// ResXDataNode. A node saved with a BinaryFormatter mimetype parses fine but
/// throws when its value is requested, since Basalt does not deserialize
/// BinaryFormatter payloads.
/// </remarks>
public class ResXResourceReader : IEnumerable, IDisposable, IResourceReader
{
    private readonly Stream? _stream;
    private readonly TextReader? _textReader;
    private readonly string? _fileContents;
    private readonly string? _fileName;
    private readonly ITypeResolutionService? _typeResolver;
    private readonly AssemblyName[]? _assemblyNames;

    private List<ResXDataNode>? _resources;
    private List<ResXDataNode>? _metadata;
    private bool _disposed;

    public ResXResourceReader(Stream stream) => _stream = stream;

    public ResXResourceReader(Stream stream, ITypeResolutionService typeResolver) : this(stream) => _typeResolver = typeResolver;

    public ResXResourceReader(Stream stream, AssemblyName[] assemblyNames) : this(stream) => _assemblyNames = assemblyNames;

    public ResXResourceReader(TextReader reader) => _textReader = reader;

    public ResXResourceReader(TextReader reader, ITypeResolutionService typeResolver) : this(reader) => _typeResolver = typeResolver;

    public ResXResourceReader(TextReader reader, AssemblyName[] assemblyNames) : this(reader) => _assemblyNames = assemblyNames;

    public ResXResourceReader(string fileName) => _fileName = fileName;

    public ResXResourceReader(string fileName, ITypeResolutionService typeResolver) : this(fileName) => _typeResolver = typeResolver;

    public ResXResourceReader(string fileName, AssemblyName[] assemblyNames) : this(fileName) => _assemblyNames = assemblyNames;

    private ResXResourceReader(string fileContents, ITypeResolutionService? typeResolver, AssemblyName[]? assemblyNames)
    {
        _fileContents = fileContents;
        _typeResolver = typeResolver;
        _assemblyNames = assemblyNames;
    }

    public string? BasePath { get; set; }

    public bool UseResXDataNodes { get; set; }

    public static ResXResourceReader FromFileContents(string fileContents) => new(fileContents, null, null);

    public static ResXResourceReader FromFileContents(string fileContents, ITypeResolutionService typeResolver) =>
        new(fileContents, typeResolver, null);

    public static ResXResourceReader FromFileContents(string fileContents, AssemblyName[] assemblyNames) =>
        new(fileContents, null, assemblyNames);

    public void Close() => ((IDisposable)this).Dispose();

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (!disposing) return;

        _stream?.Dispose();
        _textReader?.Dispose();
    }

    ~ResXResourceReader()
    {
        Dispose(false);
    }

    public IDictionaryEnumerator GetEnumerator()
    {
        EnsureParsed();
        return new NodeEnumerator(_resources!, UseResXDataNodes, ResolveValue);
    }

    public IDictionaryEnumerator GetMetadataEnumerator()
    {
        EnsureParsed();
        return new NodeEnumerator(_metadata!, UseResXDataNodes, ResolveValue);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private object? ResolveValue(ResXDataNode node) =>
        _typeResolver is not null ? node.GetValue(_typeResolver) : node.GetValue(_assemblyNames);

    private void EnsureParsed()
    {
        if (_resources is not null) return;

        _resources = [];
        _metadata = [];

        var (source, owned) = OpenDocumentSource();
        using (owned)
        {
            // A .resx may come from anywhere: no DTD, no external entity is ever resolved while reading it.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = source is Stream streamSource
                ? XmlReader.Create(streamSource, settings)
                : XmlReader.Create((TextReader)source, settings);
            var document = XDocument.Load(reader, LoadOptions.SetLineInfo);

            var root = document.Root ?? throw new ArgumentException("The .resx document has no root element.");

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var assemblyElement in root.Elements("assembly"))
            {
                var alias = (string?)assemblyElement.Attribute("alias");
                var fullName = (string?)assemblyElement.Attribute("name");
                if (alias is not null && fullName is not null) aliases[alias] = fullName;
            }

            foreach (var element in root.Elements())
            {
                if (element.Name.LocalName == "data") _resources.Add(ReadDataNode(element, aliases));
                else if (element.Name.LocalName == "metadata") _metadata.Add(ReadDataNode(element, aliases));
            }
        }
    }

    private ResXDataNode ReadDataNode(XElement element, Dictionary<string, string> aliases)
    {
        var name = (string?)element.Attribute("name") ?? string.Empty;
        var typeName = (string?)element.Attribute("type");
        var mimeType = (string?)element.Attribute("mimetype");
        var valueElement = element.Element("value");
        var rawValue = valueElement is not null ? valueElement.Value : element.Value;
        var comment = (string?)element.Element("comment");

        if (typeName is not null) typeName = ResolveAlias(typeName, aliases);

        var lineInfo = (IXmlLineInfo)element;
        var position = lineInfo.HasLineInfo() ? new Point(lineInfo.LineNumber, lineInfo.LinePosition) : Point.Empty;

        return new ResXDataNode(name, rawValue, typeName, mimeType, comment, position, BasePath);
    }

    /// <summary>"TypeName, aliasName" becomes "TypeName, Full.Assembly.Name" when aliasName was declared by an &lt;assembly&gt; element; left untouched otherwise (mscorlib shorthand, or already assembly-qualified).</summary>
    private static string ResolveAlias(string typeName, Dictionary<string, string> aliases)
    {
        var comma = typeName.IndexOf(',');
        if (comma < 0) return typeName;

        var assemblyPart = typeName[(comma + 1)..].Trim();
        return aliases.TryGetValue(assemblyPart, out var fullName) ? $"{typeName[..comma]}, {fullName}" : typeName;
    }

    /// <summary>The thing to parse, and what (if anything) EnsureParsed must dispose when done: an opened file owns its stream, file contents own their StringReader, a caller-supplied Stream/TextReader is left alone.</summary>
    private (object Source, IDisposable Owned) OpenDocumentSource()
    {
        if (_stream is not null) return (_stream, NoopDisposable.Instance);
        if (_textReader is not null) return (_textReader, NoopDisposable.Instance);

        if (_fileContents is not null)
        {
            var reader = new StringReader(_fileContents);
            return (reader, reader);
        }

        var fileStream = File.OpenRead(_fileName!);
        BasePath ??= Path.GetDirectoryName(Path.GetFullPath(_fileName!));
        return (fileStream, fileStream);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>Turns the parsed list into name/value pairs, resolving each node's value unless UseResXDataNodes keeps the node itself as the value.</summary>
    private sealed class NodeEnumerator(List<ResXDataNode> nodes, bool useNodes, Func<ResXDataNode, object?> resolve) : IDictionaryEnumerator
    {
        private int _index = -1;

        public bool MoveNext() => ++_index < nodes.Count;

        public void Reset() => _index = -1;

        public object Key => nodes[_index].Name;

        public object? Value => useNodes ? nodes[_index] : resolve(nodes[_index]);

        public DictionaryEntry Entry => new(Key, Value);

        public object Current => Entry;
    }
}
