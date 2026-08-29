using System.Xml.Linq;

namespace Basalt.Designer.Model;

/// <summary>
/// Keeping the markup the designer writes readable.
/// </summary>
/// <remarks>
/// The document is loaded with whitespace preserved, so that a person's
/// indentation, blank lines and comments survive being opened in the designer.
/// The cost is that nothing indents what the designer adds: a control dropped
/// on a form arrived hard against its neighbour's closing tag, on the wrong
/// line and at no indent at all, and the file grew steadily uglier the more
/// the designer was used.
///
/// So the whitespace is written deliberately, copied from what the file
/// already does rather than imposed: a file indented with four spaces goes on
/// being indented with four.
/// </remarks>
public static class XamlFormatting
{
    /// <summary>Adds a child at the end, indented like the ones already there.</summary>
    public static void AddIndented(XElement parent, XElement child)
    {
        var indent = IndentFor(parent);

        // The closing tag of the parent sits on its own line, and its
        // whitespace is the last node: the new child goes before that, or the
        // parent's tag ends up beside it.
        if (parent.LastNode is XText trailing && IsWhitespace(trailing))
        {
            trailing.AddBeforeSelf(new XText(indent));
            trailing.AddBeforeSelf(child);
            return;
        }

        // A parent written all on one line — <StackPanel></StackPanel> — gets
        // opened out, since a child on the same line would be unreadable.
        parent.Add(new XText(indent));
        parent.Add(child);
        parent.Add(new XText(ClosingIndentFor(parent)));
    }

    /// <summary>Puts a newly inserted child on its own line before another.</summary>
    public static void IndentBefore(XElement inserted, XElement follows)
    {
        // Whatever separates the following element from what came before it
        // is what should now separate it from the new one.
        var indent = follows.PreviousNode is XText gap && IsWhitespace(gap)
            ? gap.Value
            : IndentFor(inserted.Parent);

        inserted.AddAfterSelf(new XText(indent));
    }

    /// <summary>
    /// Removes an element and the whitespace that was put in with it.
    /// </summary>
    /// <remarks>
    /// Taking the element alone leaves its indentation behind, so an insert
    /// undone twenty times leaves twenty blank lines: the file gets worse
    /// every time the user changes their mind.
    /// </remarks>
    public static void RemoveWithWhitespace(XElement? element)
    {
        if (element is null) return;

        // The whitespace before it, which is the line it was put on. The one
        // after belongs to whatever follows.
        if (element.PreviousNode is XText before && IsWhitespace(before)) before.Remove();

        element.Remove();
    }

    /// <summary>
    /// Empties a container that has nothing left but blank lines.
    /// </summary>
    /// <remarks>
    /// Removing the last control leaves the indentation that surrounded it,
    /// so the panel closes several blank lines below where it opened and
    /// reads as damaged. With nothing inside, it should close on its own tag
    /// the way an empty one written by hand does.
    /// </remarks>
    public static void TidyIfEmpty(XElement container)
    {
        if (container.Elements().Any()) return;

        // Only whitespace left, and only whitespace is safe to drop: text
        // content is what a TextBlock says, and comments are the author's.
        if (container.Nodes().Any(n => n is not XText text || !IsWhitespace(text))) return;

        container.RemoveNodes();
    }

    /// <summary>
    /// Puts every child of a container back on its own line, indented.
    /// </summary>
    /// <remarks>
    /// Used where children have been moved about: an element put back by an
    /// undo, or one whose neighbours have gone, ends up with whatever
    /// whitespace happened to survive around it. Rewriting the separators is
    /// simpler than tracking which of them still apply, and it only ever
    /// touches whitespace between elements.
    /// </remarks>
    public static void Reindent(XElement container)
    {
        var children = container.Elements().ToList();

        if (children.Count == 0)
        {
            TidyIfEmpty(container);
            return;
        }

        var indent = IndentFor(container);

        foreach (var child in children)
        {
            // Whatever sits before it, replaced by the one indent every
            // sibling shares.
            if (child.PreviousNode is XText before && IsWhitespace(before)) before.Remove();

            child.AddBeforeSelf(new XText(indent));
        }

        // And the closing tag back on its own line.
        if (children[^1].NextNode is XText after && IsWhitespace(after)) after.Remove();

        children[^1].AddAfterSelf(new XText(ClosingIndentFor(container)));
    }

    /// <summary>
    /// The line break and indentation a child of this element should carry.
    /// </summary>
    /// <remarks>
    /// Read from a child that is already there, so the file's own style wins:
    /// two spaces, four, or tabs. Falling back to two spaces per level only
    /// where there is nothing to copy.
    /// </remarks>
    private static string IndentFor(XElement? parent)
    {
        if (parent is null) return "\n";

        foreach (var node in parent.Nodes())
        {
            if (node is XText text
                && IsWhitespace(text)
                && text.Value.Contains('\n')
                && text.NextNode is XElement)
            {
                return text.Value;
            }
        }

        // Nothing to copy: worked out from how deep the parent is, in the
        // file's own unit where that can be told.
        return "\n" + new string(' ', (Depth(parent) + 1) * UnitFor(parent));
    }

    /// <summary>The whitespace that should precede this element's closing tag.</summary>
    private static string ClosingIndentFor(XElement parent) =>
        "\n" + new string(' ', Depth(parent) * UnitFor(parent));

    /// <summary>How far in from the root an element sits.</summary>
    private static int Depth(XElement element) => element.Ancestors().Count();

    /// <summary>
    /// How many spaces one level of indentation is in this file.
    /// </summary>
    /// <remarks>
    /// Measured from the first indented line rather than assumed: a file
    /// written with four spaces should not start growing two-space children
    /// halfway down.
    /// </remarks>
    private static int UnitFor(XElement element)
    {
        var root = element.Document?.Root ?? element;

        foreach (var node in root.DescendantNodes())
        {
            if (node is not XText text || !IsWhitespace(text)) continue;

            var line = text.Value;
            var breakAt = line.LastIndexOf('\n');

            if (breakAt < 0) continue;

            var spaces = line.Length - breakAt - 1;

            if (spaces > 0) return spaces;
        }

        return 2;
    }

    /// <summary>Whether a text node is only layout, not content.</summary>
    private static bool IsWhitespace(XText text) =>
        text.Value.Length > 0 && text.Value.All(char.IsWhiteSpace);
}
