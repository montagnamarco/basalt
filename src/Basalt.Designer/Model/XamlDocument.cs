using System.Xml.Linq;

namespace Basalt.Designer.Model;

/// <summary>
/// .axaml document manipulated by the designer.
///
/// The XAML file stays the source of truth: the designer edits the existing
/// XML tree instead of regenerating it from a parallel model. That way the
/// formatting, the comments and the hand-written markup the designer does not
/// understand survive an open-and-save round trip.
/// </summary>
public sealed class XamlDocument
{
    public static readonly XNamespace AvaloniaNs = "https://github.com/avaloniaui";
    public static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private XamlDocument(XDocument xml, string? filePath)
    {
        Xml = xml;
        FilePath = filePath;
        Indent = MeasureIndent(xml);
    }

    /// <summary>
    /// How far one level is indented in this file.
    /// </summary>
    /// <remarks>
    /// From the first line that is indented at all. A file written with four
    /// spaces should not come back with two the first time it is saved.
    /// </remarks>
    private static int MeasureIndent(XDocument xml)
    {
        foreach (var node in xml.DescendantNodes().OfType<XText>())
        {
            if (!node.Value.All(char.IsWhiteSpace)) continue;

            var lastBreak = node.Value.LastIndexOf('\n');
            if (lastBreak < 0) continue;

            var spaces = node.Value.Length - lastBreak - 1;

            if (spaces > 0) return spaces;
        }

        return 2;
    }

    public XDocument Xml { get; }
    public string? FilePath { get; private set; }
    public XElement Root => Xml.Root ?? throw new InvalidOperationException("Documento XAML senza radice.");

    /// <summary>Name of the code-behind class, that is the x:Class attribute.</summary>
    public string? ClassName => Root.Attribute(XamlNs + "Class")?.Value;

    public event EventHandler? Changed;

    public static XamlDocument Parse(string xaml, string? filePath = null)
    {
        // PreserveWhitespace keeps the author's original indentation.
        var xml = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return new XamlDocument(xml, filePath);
    }

    public static XamlDocument Load(string filePath) =>
        Parse(File.ReadAllText(filePath), filePath);

    public async Task SaveAsync(string? filePath = null, CancellationToken ct = default)
    {
        var target = filePath ?? FilePath
            ?? throw new InvalidOperationException("No path was given to save to.");

        await File.WriteAllTextAsync(target, ToXaml(), ct).ConfigureAwait(false);
        FilePath = target;
    }

    /// <summary>
    /// The document as markup, laid out to be read.
    /// </summary>
    /// <remarks>
    /// Formatted here because this is the one place every path goes through —
    /// saving, the markup half of the tab, and the build. Writing the tree
    /// out verbatim kept whatever shape the file happened to have, so a form
    /// written on one line stayed on one line no matter how much the designer
    /// added to it.
    ///
    /// <see cref="Indent"/> is the file's own, measured when it was opened,
    /// so a project indented with four spaces stays that way.
    /// </remarks>
    public string ToXaml() => XamlFormatter.Format(Xml, Indent);

    /// <summary>
    /// Spaces per level, taken from the file as it was opened.
    /// </summary>
    /// <remarks>
    /// Measured once rather than on every write: after the first format the
    /// document is in this style anyway, and re-measuring would only ever
    /// confirm it.
    /// </remarks>
    public int Indent { get; private set; } = 2;

    /// <summary>
    /// The name Avalonia uses to bind the element to the code-behind.
    /// In Avalonia x:Name and Name are equivalent; x:Name takes precedence.
    /// </summary>
    public static string? GetName(XElement element) =>
        element.Attribute(XamlNs + "Name")?.Value ?? element.Attribute("Name")?.Value;

    public static void SetName(XElement element, string? name)
    {
        element.Attribute(XamlNs + "Name")?.Remove();
        element.Attribute("Name")?.Remove();
        if (!string.IsNullOrWhiteSpace(name))
            element.SetAttributeValue(XamlNs + "Name", name);
    }

    /// <summary>
    /// Elements that represent controls, excluding nested property syntax
    /// such as &lt;Grid.RowDefinitions&gt;, recognisable by the dot in the name.
    /// </summary>
    public static IEnumerable<XElement> ControlChildren(XElement element) =>
        element.Elements().Where(e => !e.Name.LocalName.Contains('.'));

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
