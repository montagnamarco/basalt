using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace;

/// <summary>
/// Reads resource text without resolving types, converters, or file references. Visual Studio also
/// stores designer flags as &lt;metadata&gt; elements, such as "$this.Localizable"; they are kept apart from
/// the &lt;data&gt; values in <see cref="Metadata"/> so no value reader can mistake one for the other.
/// </summary>
public sealed class WinFormsResourceDocument
{
    private readonly SourceText _text;
    private readonly Dictionary<string, ResourceNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceNode> _metadataNodes = new(StringComparer.Ordinal);
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
        // The reader and the DOM list each kind of element in document order, so the N-th <data> the
        // reader meets is the N-th <data> element of the DOM, and likewise for <metadata>.
        var dataElements = document.Root?.Elements("data").ToArray() ?? [];
        var metadataElements = document.Root?.Elements("metadata").ToArray() ?? [];
        _newLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, ReaderSettings());
        var entries = new List<WinFormsResourceEntry>();
        var metadata = new List<WinFormsResourceEntry>();
        ResourceStart? current = null;
        var foundRoot = false;
        var closingOffset = -1;
        void AddResource(ResourceStart start, int end)
        {
            if (start.Kind == "data")
            {
                AddNode(_nodes, entries, start.Offset, end, dataElements[entries.Count]);
            }
            else
            {
                AddNode(_metadataNodes, metadata, start.Offset, end, metadataElements[metadata.Count]);
            }
        }
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
            else if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name is "data" or "metadata")
            {
                var start = Offset(reader) - 1;
                current = new ResourceStart(start, reader.Name);
                if (reader.IsEmptyElement)
                {
                    AddResource(current, FindTagEnd(start) + 1);
                    current = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 1 && current is { } pending
                && reader.Name == pending.Kind)
            {
                AddResource(pending, FindTagEnd(Offset(reader) - 2) + 1);
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
        Metadata = metadata.AsReadOnly();
    }

    public string Source { get; }
    public IReadOnlyList<WinFormsResourceEntry> Entries { get; }

    /// <summary>The &lt;metadata&gt; elements in document order. Their values are not resource values.</summary>
    public IReadOnlyList<WinFormsResourceEntry> Metadata { get; }

    public WinFormsResourceEntry? TryGet(string name) =>
        _nodes.TryGetValue(name, out var node) ? node.Entry : null;

    /// <summary>The metadata element with this name, such as Visual Studio's "$this.Localizable", or null.</summary>
    public WinFormsResourceEntry? TryGetMetadata(string name) =>
        _metadataNodes.TryGetValue(name, out var node) ? node.Entry : null;

    public string SetString(string name, string value) => SetValue(name, value, null);

    /// <summary>Stores raw text and metadata; no interpretation of the resource type occurs.</summary>
    public string SetValue(string name, string value, string? typeName, string? mimeType = null)
        => SetValueCore(name, value, typeName, mimeType, null, updateComment: false);

    /// <summary>Updates a resource comment along with its raw value and metadata.</summary>
    public string SetValue(string name, string value, string? typeName, string? mimeType, string? comment)
        => SetValueCore(name, value, typeName, mimeType, comment, updateComment: true);

    /// <summary>
    /// Stores a metadata element such as Visual Studio's "$this.Localizable" (type System.Boolean, value True).
    /// An existing element is rewritten in place. A new one goes ahead of the first resource element, the way
    /// Visual Studio places designer metadata, so no unrelated byte moves.
    /// </summary>
    public string SetMetadata(string name, string value, string? typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        if (_metadataNodes.TryGetValue(name, out var existing))
        {
            if (existing.Entry.Value == value && existing.Entry.TypeName == typeName) return Source;
            var element = new XElement(existing.Element);
            element.SetAttributeValue("type", typeName);
            var valueElement = element.Element("value");
            if (valueElement is null) element.AddFirst(new XElement("value", value));
            else valueElement.Value = value;
            return Replace(existing.Span, Markup(element));
        }
        var added = new XElement("metadata", new XAttribute("name", name), new XElement("value", value));
        added.SetAttributeValue("type", typeName);
        return InsertBeforeFirstResource(added);
    }

    /// <summary>
    /// Removes a metadata element together with the line it fills, so no blank line is left behind when
    /// Visual Studio would have dropped the flag. A name that is absent leaves the text unchanged.
    /// </summary>
    public string RemoveMetadata(string name)
    {
        if (!_metadataNodes.TryGetValue(name, out var node)) return Source;
        return Replace(RemovalSpan(node.Span), "");
    }

    /// <summary>
    /// Removes every data entry under one of the prefixes: "Button1" takes "Button1.Text" and "Button1.Location",
    /// never "Button10.Text". Entries are removed with the lines they fill, so a removed control leaves no blank line.
    /// </summary>
    public string RemoveEntriesUnder(IEnumerable<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        var dotted = prefixes.Select(prefix => prefix + ".").ToArray();
        var changes = _nodes.Values
            .Where(node => dotted.Any(prefix => node.Entry.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(node => node.Span.Start)
            .Select(node => new TextChange(RemovalSpan(node.Span), ""))
            .ToArray();
        return changes.Length == 0 ? Source : _text.WithChanges(changes).ToString();
    }

    /// <summary>
    /// Renames every data entry under <paramref name="oldPrefix"/> to <paramref name="newPrefix"/> in place, so each
    /// element keeps its position and its content. An entry already under the new prefix is removed first: a stale
    /// one would leave two entries with one name, which the reader rejects.
    /// </summary>
    public string RenameEntries(string oldPrefix, string newPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldPrefix);
        ArgumentException.ThrowIfNullOrEmpty(newPrefix);
        var oldDotted = oldPrefix + ".";
        var newDotted = newPrefix + ".";
        var changes = new List<TextChange>();
        foreach (var node in _nodes.Values.OrderBy(node => node.Span.Start))
        {
            var name = node.Entry.Name;
            if (name.StartsWith(newDotted, StringComparison.Ordinal))
            {
                changes.Add(new TextChange(RemovalSpan(node.Span), ""));
            }
            else if (name.StartsWith(oldDotted, StringComparison.Ordinal))
            {
                var element = new XElement(node.Element);
                element.SetAttributeValue("name", newPrefix + name[oldPrefix.Length..]);
                changes.Add(new TextChange(node.Span, Markup(element)));
            }
        }
        return changes.Count == 0 ? Source : _text.WithChanges(changes).ToString();
    }

    /// <summary>
    /// The span a removal takes: the element's whole lines when nothing else shares them, else the element alone, so
    /// no blank line is left where a value was.
    /// </summary>
    private TextSpan RemovalSpan(TextSpan span)
    {
        var first = _text.Lines.GetLineFromPosition(span.Start);
        var last = _text.Lines.GetLineFromPosition(span.End);
        var alone = Source[first.Start..span.Start].All(char.IsWhiteSpace)
            && Source[span.End..last.End].All(char.IsWhiteSpace);
        return alone ? TextSpan.FromBounds(first.Start, last.EndIncludingLineBreak) : span;
    }

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
            return Replace(existing.Span, Markup(element));
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
            return Replace(emptySpan, Markup(root));
        }
        return Replace(new TextSpan(_rootClosingOffset, 0),
            "  " + Markup(added) + _newLine);
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

    /// <summary>
    /// Serializes an element for the document. A carriage return is written as &amp;#xD;: a raw one would be
    /// normalized to a line feed by the next XML read, which would change a multi-line value on every reopen.
    /// Every other line break takes the document's own newline: the XML reader has already turned the file's
    /// CRLF line breaks, inside the element, into line feeds, so writing them back raw would mix the two.
    /// </summary>
    private string Markup(XElement element)
    {
        // XElement.ToString rewrites line breaks in text to the platform's, which drops the carriage return
        // of a Windows line break. NewLineHandling.None keeps the value's own characters for the escape below.
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.None,
        };
        var builder = new System.Text.StringBuilder();
        using (var writer = XmlWriter.Create(builder, settings))
        {
            element.WriteTo(writer);
        }
        return builder.ToString()
            .Replace("\r", "&#xD;", StringComparison.Ordinal)
            .Replace("\n", _newLine, StringComparison.Ordinal);
    }

    private string Replace(TextSpan span, string replacement) =>
        _text.WithChanges(new TextChange(span, replacement)).ToString();

    /// <summary>
    /// Writes a metadata element ahead of the first data or metadata element, indented like that element.
    /// A document with no resource element yet takes it at the closing root, or inside an empty root.
    /// </summary>
    private string InsertBeforeFirstResource(XElement element)
    {
        if (_emptyRootSpan is { } emptySpan)
        {
            var root = new XElement(_emptyRoot!);
            root.Add(element);
            return Replace(emptySpan, Markup(root));
        }
        var starts = _nodes.Values.Concat(_metadataNodes.Values).Select(node => node.Span.Start).ToArray();
        if (starts.Length == 0)
        {
            return Replace(new TextSpan(_rootClosingOffset, 0),
                "  " + Markup(element) + _newLine);
        }
        var first = starts.Min();
        var line = _text.Lines.GetLineFromPosition(first).Start;
        var indentation = Source[line..first];
        if (!indentation.All(char.IsWhiteSpace))
        {
            return Replace(new TextSpan(_rootClosingOffset, 0),
                "  " + Markup(element) + _newLine);
        }
        return Replace(new TextSpan(line, 0), indentation + Markup(element) + _newLine);
    }

    private void AddNode(Dictionary<string, ResourceNode> nodes, List<WinFormsResourceEntry> list,
        int start, int end, XElement element)
    {
        var span = TextSpan.FromBounds(start, end);
        var name = (string?)element.Attribute("name")
            ?? throw new FormatException("A resource entry has no name.");
        var entry = new WinFormsResourceEntry(name, (string?)element.Attribute("type"),
            (string?)element.Attribute("mimetype"), element.Element("value")?.Value ?? "",
            (string?)element.Element("comment"));
        if (!nodes.TryAdd(name, new ResourceNode(entry, element, span)))
        {
            throw new FormatException("The resource document contains duplicate entry names.");
        }
        list.Add(entry);
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

    private sealed record ResourceStart(int Offset, string Kind);
    private sealed record ResourceNode(WinFormsResourceEntry Entry, XElement Element, TextSpan Span);
}

public sealed record WinFormsResourceEntry(string Name, string? TypeName, string? MimeType, string Value, string? Comment);
