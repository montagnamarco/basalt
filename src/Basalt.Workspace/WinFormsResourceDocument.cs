using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace;

/// <summary>Reads resource text without resolving types, converters, or file references.</summary>
public sealed class WinFormsResourceDocument
{
    private readonly SourceText _text;
    private readonly Dictionary<string, ResourceNode> _nodes = new(StringComparer.Ordinal);
    private readonly int _rootClosingOffset;
    private readonly string _newLine;
    private readonly IReadOnlyList<int> _lineStarts;
    private readonly TextSpan? _emptyRootSpan;
    private readonly XElement? _emptyRoot;

    public WinFormsResourceDocument(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _text = SourceText.From(source);
        _lineStarts = XmlLineStarts(source);
        using var documentInput = new StringReader(source);
        using var documentReader = XmlReader.Create(documentInput, ReaderSettings());
        var document = XDocument.Load(documentReader, LoadOptions.PreserveWhitespace);
        var elements = document.Root?.Elements("data").ToArray() ?? [];
        _newLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, ReaderSettings());
        var entries = new List<WinFormsResourceEntry>();
        ResourceStart? current = null;
        var foundRoot = false;
        var closingOffset = -1;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0)
            {
                if (reader.Name != "root" || reader.NamespaceURI.Length != 0)
                {
                    throw new FormatException("A resource document requires an unqualified root element.");
                }
                foundRoot = true;
                if (reader.IsEmptyElement)
                {
                    var start = Offset(reader) - 1;
                    _emptyRootSpan = TextSpan.FromBounds(start, FindTagEnd(start) + 1);
                    _emptyRoot = document.Root;
                    closingOffset = start;
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "data")
            {
                var start = Offset(reader) - 1;
                current = new ResourceStart(start);
                if (reader.IsEmptyElement)
                {
                    AddNode(start, FindTagEnd(start) + 1, entries, elements[entries.Count]);
                    current = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 1 && reader.Name == "data" && current is { } pending)
            {
                AddNode(pending.Offset, FindTagEnd(Offset(reader) - 2) + 1, entries, elements[entries.Count]);
                current = null;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 0)
            {
                closingOffset = Offset(reader) - 2;
            }
        }
        if (!foundRoot || closingOffset < 0)
        {
            throw new FormatException("The resource document has no closing root element.");
        }
        _rootClosingOffset = closingOffset;
        Entries = entries.AsReadOnly();
    }

    public string Source { get; }
    public IReadOnlyList<WinFormsResourceEntry> Entries { get; }

    public WinFormsResourceEntry? TryGet(string name) =>
        _nodes.TryGetValue(name, out var node) ? node.Entry : null;

    public string SetString(string name, string value) => SetValue(name, value, null);

    /// <summary>Stores raw text and metadata; no interpretation of the resource type occurs.</summary>
    public string SetValue(string name, string value, string? typeName, string? mimeType = null)
        => SetValueCore(name, value, typeName, mimeType, null, updateComment: false);

    /// <summary>Updates a resource comment along with its raw value and metadata.</summary>
    public string SetValue(string name, string value, string? typeName, string? mimeType, string? comment)
        => SetValueCore(name, value, typeName, mimeType, comment, updateComment: true);

    private string SetValueCore(string name, string value, string? typeName, string? mimeType,
        string? comment, bool updateComment)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        if (_nodes.TryGetValue(name, out var existing))
        {
            if (existing.Entry.Value == value && existing.Entry.TypeName == typeName
                && existing.Entry.MimeType == mimeType
                && (!updateComment || existing.Entry.Comment == comment))
            {
                return Source;
            }
            var element = new XElement(existing.Element);
            if (existing.Entry.TypeName != typeName)
            {
                element.SetAttributeValue("type", typeName);
            }
            if (existing.Entry.MimeType != mimeType)
            {
                element.SetAttributeValue("mimetype", mimeType);
            }
            if (existing.Entry.Value != value)
            {
                element.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                var valueElement = element.Element("value");
                if (valueElement is null)
                {
                    element.AddFirst(new XElement("value", value));
                }
                else
                {
                    valueElement.Value = value;
                }
            }
            if (updateComment)
            {
                UpdateComment(element, comment);
            }
            return Replace(existing.Span, element.ToString(SaveOptions.DisableFormatting));
        }
        var added = new XElement("data", new XAttribute("name", name),
            new XAttribute(XNamespace.Xml + "space", "preserve"), new XElement("value", value));
        added.SetAttributeValue("type", typeName);
        added.SetAttributeValue("mimetype", mimeType);
        if (updateComment)
        {
            UpdateComment(added, comment);
        }
        if (_emptyRootSpan is { } emptySpan)
        {
            var root = new XElement(_emptyRoot!);
            root.Add(added);
            return Replace(emptySpan, root.ToString(SaveOptions.DisableFormatting));
        }
        return Replace(new TextSpan(_rootClosingOffset, 0),
            "  " + added.ToString(SaveOptions.DisableFormatting) + _newLine);
    }

    private static void UpdateComment(XElement element, string? comment)
    {
        var existing = element.Element("comment");
        if (comment is null)
        {
            existing?.Remove();
        }
        else if (existing is not null)
        {
            existing.Value = comment;
        }
        else if (element.Element("value") is { } value)
        {
            value.AddAfterSelf(new XElement("comment", comment));
        }
        else
        {
            element.Add(new XElement("comment", comment));
        }
    }

    public string Remove(string name) => _nodes.TryGetValue(name, out var node)
        ? Replace(node.Span, "") : Source;

    private string Replace(TextSpan span, string replacement) =>
        _text.WithChanges(new TextChange(span, replacement)).ToString();

    private void AddNode(int start, int end, List<WinFormsResourceEntry> entries, XElement element)
    {
        var span = TextSpan.FromBounds(start, end);
        var name = (string?)element.Attribute("name")
            ?? throw new FormatException("A resource entry has no name.");
        var entry = new WinFormsResourceEntry(name, (string?)element.Attribute("type"),
            (string?)element.Attribute("mimetype"), element.Element("value")?.Value ?? "",
            (string?)element.Element("comment"));
        if (!_nodes.TryAdd(name, new ResourceNode(entry, element, span)))
        {
            throw new FormatException("The resource document contains duplicate entry names.");
        }
        entries.Add(entry);
    }

    private int Offset(XmlReader reader)
    {
        var location = (IXmlLineInfo)reader;
        return _lineStarts[location.LineNumber - 1] + location.LinePosition - 1;
    }

    private static IReadOnlyList<int> XmlLineStarts(string source)
    {
        // XML counts CR/LF lines; Roslyn also counts Unicode line separators.
        // Resource values can contain those separators without starting an XML line.
        var starts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n')
                {
                    index++;
                }
                starts.Add(index + 1);
            }
            else if (source[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }
        return starts;
    }

    private int FindTagEnd(int start)
    {
        // XmlReader identifies the token. Scan only its lexical boundary so edits
        // preserve every byte of unrelated XML, including comments and whitespace.
        var quote = '\0';
        for (var index = start; index < Source.Length; index++)
        {
            var character = Source[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index;
            }
        }
        throw new FormatException("The resource XML token has no closing delimiter.");
    }

    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private sealed record ResourceStart(int Offset);
    private sealed record ResourceNode(WinFormsResourceEntry Entry, XElement Element, TextSpan Span);
}

public sealed record WinFormsResourceEntry(string Name, string? TypeName, string? MimeType, string Value, string? Comment);
