using System.Text;
using System.Xml.Linq;

namespace Basalt.Designer.Model;

/// <summary>
/// Writes a whole XAML document out, laid out to be read.
/// </summary>
/// <remarks>
/// <see cref="XamlFormatting"/> indents what the designer adds, one edit at a
/// time, which keeps a tidy file tidy. It cannot rescue a file that is not:
/// markup written on one line stays on one line however many controls are
/// dropped into it, because nothing ever revisits what is already there.
///
/// This does the other half — every element on its own line, nested by depth,
/// which is what makes the shape of a form legible at a glance.
///
/// Written by hand rather than through <c>XDocument.Save</c>, which cannot
/// break attributes across lines and treats every whitespace node as content
/// once the document was loaded with whitespace preserved. The formatter has
/// to decide what is layout and what is text, and only the caller knows.
/// </remarks>
public static class XamlFormatter
{
    /// <summary>How wide a line may get before attributes go one per line.</summary>
    private const int Width = 100;

    /// <summary>
    /// Formats a document.
    /// </summary>
    /// <param name="indent">Spaces per level; the file's own where known.</param>
    public static string Format(XDocument document, int indent = 2)
    {
        if (document.Root is not { } root) return "";

        var text = new StringBuilder();

        if (document.Declaration is { } declaration)
            text.Append(declaration).Append('\n');

        Write(text, root, 0, indent);

        return text.ToString().TrimEnd() + "\n";
    }

    /// <summary>Formats the document behind a <see cref="XamlDocument"/>.</summary>
    public static string Format(XamlDocument document, int indent = 2) =>
        Format(document.Xml, indent);

    private static void Write(StringBuilder text, XElement element, int depth, int unit)
    {
        var pad = new string(' ', depth * unit);

        var open = OpenTag(element, pad, unit);

        // Nothing inside: the tag closes itself, which is how an empty
        // control is written everywhere else.
        if (!element.Nodes().Any(IsContent))
        {
            text.Append(pad).Append(open).Append(" />\n");
            return;
        }

        // Text and nothing else — <TextBlock>Ciao</TextBlock> — stays on one
        // line: breaking it would put whitespace inside the text, which is
        // not layout but what the control says.
        if (OnlyText(element, out var content))
        {
            text.Append(pad).Append(open).Append('>')
                .Append(Escape(content))
                .Append("</").Append(element.Name.LocalName).Append(">\n");
            return;
        }

        text.Append(pad).Append(open).Append(">\n");

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    Write(text, child, depth + 1, unit);
                    break;

                case XComment comment:
                    text.Append(new string(' ', (depth + 1) * unit))
                        .Append("<!--").Append(comment.Value).Append("-->\n");
                    break;

                // Text among elements is content the author put there, and
                // it keeps its own line rather than being folded away.
                case XText inner when !IsWhitespace(inner):
                    text.Append(new string(' ', (depth + 1) * unit))
                        .Append(Escape(inner.Value.Trim())).Append('\n');
                    break;
            }
        }

        text.Append(pad).Append("</").Append(element.Name.LocalName).Append(">\n");
    }

    /// <summary>
    /// The opening tag with its attributes, broken across lines if long.
    /// </summary>
    /// <remarks>
    /// A root element carries the namespaces and often a title and a size,
    /// which on one line runs past any window: those go one per line, lined
    /// up under the first. Short tags stay whole, since a Button with one
    /// attribute reads worse split in two.
    /// </remarks>
    private static string OpenTag(XElement element, string pad, int unit)
    {
        var name = QualifiedName(element);
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration || Declares(element, a)).ToList();

        if (attributes.Count == 0) return "<" + name;

        var written = attributes.Select(Attribute).ToList();

        var oneLine = "<" + name + " " + string.Join(" ", written);

        if (pad.Length + oneLine.Length <= Width) return oneLine;

        // Under the element's own name, which is where the eye already is.
        var aligned = "\n" + pad + new string(' ', name.Length + 2);

        return "<" + name + " " + string.Join(aligned, written);
    }

    /// <summary>Whether a namespace declaration belongs on this element.</summary>
    /// <remarks>
    /// LINQ-to-XML reports inherited namespaces on every element that uses
    /// them; only the one that actually declared it should write it out, or
    /// every child repeats the whole set.
    /// </remarks>
    private static bool Declares(XElement element, XAttribute attribute) =>
        element.Parent is null
        || element.Parent.Attributes().All(a => a.Name != attribute.Name);

    private static string Attribute(XAttribute attribute) =>
        $"{QualifiedName(attribute)}=\"{Escape(attribute.Value)}\"";

    /// <summary>The name as it is written, prefix and all.</summary>
    private static string QualifiedName(XElement element) =>
        Prefix(element, element.Name) + element.Name.LocalName;

    private static string QualifiedName(XAttribute attribute) =>
        attribute.IsNamespaceDeclaration
            ? attribute.Name.LocalName == "xmlns"
                ? "xmlns"
                : "xmlns:" + attribute.Name.LocalName
            : Prefix(attribute.Parent, attribute.Name) + attribute.Name.LocalName;

    private static string Prefix(XElement? scope, XName name)
    {
        if (name.Namespace == XNamespace.None) return "";

        var prefix = scope?.GetPrefixOfNamespace(name.Namespace);

        return string.IsNullOrEmpty(prefix) ? "" : prefix + ":";
    }

    /// <summary>Whether an element holds text and nothing else.</summary>
    private static bool OnlyText(XElement element, out string content)
    {
        content = "";

        if (element.Elements().Any()) return false;
        if (element.Nodes().OfType<XComment>().Any()) return false;

        var text = element.Nodes().OfType<XText>().Where(t => !IsWhitespace(t)).ToList();

        if (text.Count == 0) return false;

        content = string.Concat(text.Select(t => t.Value)).Trim();

        return content.Length > 0;
    }

    /// <summary>Whether a node is worth a line of its own.</summary>
    private static bool IsContent(XNode node) =>
        node switch
        {
            XElement => true,
            XComment => true,
            XText text => !IsWhitespace(text),
            _ => false,
        };

    private static bool IsWhitespace(XText text) =>
        text.Value.Length == 0 || text.Value.All(char.IsWhiteSpace);

    /// <summary>
    /// The characters XML cannot carry raw.
    /// </summary>
    /// <remarks>
    /// Quotes are escaped because attribute values are written in double
    /// quotes; apostrophes are left alone, since escaping them would turn a
    /// perfectly readable Italian label into a row of entities.
    /// </remarks>
    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
