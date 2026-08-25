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
            ?? throw new InvalidOperationException("Nessun path indicato per il salvataggio.");

        await File.WriteAllTextAsync(target, ToXaml(), ct).ConfigureAwait(false);
        FilePath = target;
    }

    public string ToXaml() => Xml.Declaration is null
        ? Xml.ToString(SaveOptions.DisableFormatting)
        : Xml.Declaration + Environment.NewLine + Xml.ToString(SaveOptions.DisableFormatting);

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
