using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Basalt.Workspace;

/// <summary>
/// Exclude From Project and Include In Project for an SDK-style project.
///
/// An SDK-style project lists every file its default globs find, so
/// excluding a file means a <c>Remove</c> item, and including it again means
/// removing that item, or adding an <c>Include</c> when the globs do not reach
/// the file (for instance with <c>EnableDefaultCompileItems</c> turned off).
/// The file stays on disk in both cases.
/// </summary>
/// <remarks>
/// The project is read as XML, so membership is decided on its items whatever
/// their shape. The text is edited, not re-serialised, so every byte outside
/// the item the editor writes or removes stays as it was: the byte-order mark,
/// the line endings and the indentation the project already uses included.
///
/// Include removes only an item that names exactly this file and nothing else
/// (<c>&lt;Compile Remove="x" /&gt;</c> or <c>&lt;Compile Remove="x"&gt;&lt;/Compile&gt;</c>).
/// Any other <c>Remove</c> that names the file was written by hand, so it is
/// left alone and Include refuses; a glob such as <c>Old\**</c> is never
/// touched. An item group that held only the removed item goes with it. A
/// project whose closing tag shares its line with other content gets one line
/// break added before the new group, which Include does not take back.
/// </remarks>
public static class ProjectItemEditor
{
    /// <summary>Whether the project includes a file, and how.</summary>
    public enum Membership
    {
        /// <summary>The project builds the file, by its default globs or an explicit Include.</summary>
        Included,

        /// <summary>A Remove item keeps the file out of the project.</summary>
        Excluded,

        /// <summary>The default globs do not reach the file and nothing includes it.</summary>
        NotInProject
    }

    private static readonly Regex SdkStyle = new(@"<Project\b[^>]*\bSdk\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectIndent = new(@"<Project\b[^>]*>\r?\n([ \t]+)<", RegexOptions.CultureInvariant);

    private static readonly string[] ItemTypes = ["Compile", "None", "EmbeddedResource"];

    /// <summary>Whether a project file is SDK-style, the only kind whose membership this editor reads and writes.</summary>
    public static bool IsSdkStyle(string projectText) => SdkStyle.IsMatch(projectText);

    /// <summary>
    /// The item type the SDK's default globs give a file: Compile for Visual
    /// Basic, EmbeddedResource for resource files, None for everything else.
    /// </summary>
    public static string ItemTypeFor(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".vb" => "Compile",
            ".resx" => "EmbeddedResource",
            _ => "None"
        };

    /// <summary>
    /// The project-relative path of a file, with backslashes, as the
    /// project file writes it.
    /// </summary>
    public static string RelativeItem(string projectPath, string filePath) =>
        Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, Path.GetFullPath(filePath))
            .Replace('/', '\\');

    /// <summary>
    /// Whether a file is in the project, read from the project file's text.
    /// A project that is not SDK-style, or whose XML cannot be read, lists its
    /// own files, so every file in it counts as included.
    /// </summary>
    public static Membership MembershipOf(string projectPath, string projectText, string filePath)
    {
        if (!IsSdkStyle(projectText)) return Membership.Included;

        var document = Parse(projectText);
        if (document is null) return Membership.Included;

        var item = RelativeItem(projectPath, filePath);

        if (RemoveItemsFor(document, item).Any()) return Membership.Excluded;
        if (IncludeItemsFor(document, item).Any()) return Membership.Included;

        return DefaultsCover(projectText, ItemTypeFor(filePath)) ? Membership.Included : Membership.NotInProject;
    }

    /// <summary>
    /// Whether the project excludes a file by an item that was not written by
    /// this editor: a Remove naming the file with another attribute or with
    /// content. Include refuses such an item, so the file stays excluded.
    /// </summary>
    public static bool IsHandWritten(string projectPath, string filePath)
    {
        var text = ReadText(projectPath, out _);
        if (!IsSdkStyle(text) || Parse(text) is not { } document) return false;

        return RemoveItemsFor(document, RelativeItem(projectPath, filePath)).Any(e => !IsPlainRemove(e));
    }

    /// <summary>
    /// Excludes a file from the project by writing a Remove item for it in a
    /// new item group at the end of the project. Returns false when the file
    /// is already excluded, or the project is not SDK-style, so nothing is
    /// written.
    /// </summary>
    public static bool Exclude(string projectPath, string filePath)
    {
        var text = ReadText(projectPath, out var byteOrderMark);
        if (!IsSdkStyle(text) || MembershipOf(projectPath, text, filePath) == Membership.Excluded) return false;

        var item = RelativeItem(projectPath, filePath);
        var written = AppendItemGroup(text, $@"<{ItemTypeFor(filePath)} Remove=""{item}"" />");
        WriteText(projectPath, written, byteOrderMark);
        return true;
    }

    /// <summary>
    /// Includes an excluded file again. Removes the Remove item that names the
    /// file when it is one this editor could have written, and adds an Include
    /// when the default globs still do not reach it. Returns false, and writes
    /// nothing, when the file is already included or a hand-written item
    /// excludes it.
    /// </summary>
    public static bool Include(string projectPath, string filePath)
    {
        var text = ReadText(projectPath, out var byteOrderMark);
        if (!IsSdkStyle(text) || MembershipOf(projectPath, text, filePath) == Membership.Included) return false;

        var document = Parse(text)
            ?? throw new InvalidDataException("The project file is not well-formed XML.");

        var item = RelativeItem(projectPath, filePath);
        var removes = RemoveItemsFor(document, item).ToList();

        var rewritten = text;
        if (removes.Count > 0)
        {
            if (removes.Any(remove => !IsPlainRemove(remove))) return false;

            var pattern = PlainRemovePattern(item);
            if (Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count != removes.Count) return false;

            // A group that held only the Remove goes with it; a Remove inside a
            // larger group is taken out alone.
            rewritten = Regex.Replace(rewritten,
                $@"[ \t]*<ItemGroup>\s*{pattern}\s*</ItemGroup>[ \t]*\r?\n?", "", RegexOptions.IgnoreCase);
            rewritten = Regex.Replace(rewritten, $@"[ \t]*{pattern}[ \t]*\r?\n?", "", RegexOptions.IgnoreCase);
        }

        if (MembershipOf(projectPath, rewritten, filePath) == Membership.NotInProject)
        {
            rewritten = AppendItemGroup(rewritten, $@"<{ItemTypeFor(filePath)} Include=""{item}"" />");
        }

        WriteText(projectPath, rewritten, byteOrderMark);
        return true;
    }

    /// <summary>The item elements that name a path with Remove, of any item type.</summary>
    private static IEnumerable<XElement> RemoveItemsFor(XDocument document, string item) =>
        document.Descendants()
            .Where(element => ItemTypes.Contains(element.Name.LocalName)
                && element.Attribute("Remove") is { } remove
                && PathEquals(remove.Value, item));

    /// <summary>The item elements that name a path with Include, of any item type.</summary>
    private static IEnumerable<XElement> IncludeItemsFor(XDocument document, string item) =>
        document.Descendants()
            .Where(element => ItemTypes.Contains(element.Name.LocalName)
                && element.Attribute("Include") is { } include
                && PathEquals(include.Value, item));

    /// <summary>
    /// An item that names only the file in Remove and has no content but
    /// white space: the only shape Exclude writes, and the only one Include
    /// removes.
    /// </summary>
    private static bool IsPlainRemove(XElement element) =>
        element.Attributes().Count() == 1
        && element.Nodes().All(node => node is XText text && string.IsNullOrWhiteSpace(text.Value));

    /// <summary>The text of a plain Remove item for a path, in either of its two written forms.</summary>
    /// <remarks>
    /// As loose as the XML reading that decides an item is plain (IsPlainRemove,
    /// PathEquals): either quote, white space around "=", and either path
    /// separator. A stricter text pattern refused a plain item written with
    /// single quotes, and Include then failed with nothing said.
    /// </remarks>
    private static string PlainRemovePattern(string item)
    {
        var path = string.Join(@"[\\/]", item.Split('\\', '/').Select(Regex.Escape));
        return $@"<(Compile|None|EmbeddedResource)\s+Remove\s*=\s*([""']){path}\2\s*(?:/>|>\s*</\1>)";
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static XDocument? Parse(string text)
    {
        try
        {
            return XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the SDK's default globs for an item type reach the project's
    /// files: off when the project turns off the defaults, or that type's.
    /// </summary>
    private static bool DefaultsCover(string projectText, string itemType)
    {
        bool Off(string property) =>
            Regex.IsMatch(projectText, $@"<{property}>\s*false\s*</{property}>", RegexOptions.IgnoreCase);

        if (Off("EnableDefaultItems")) return false;

        return itemType switch
        {
            "Compile" => !Off("EnableDefaultCompileItems"),
            "EmbeddedResource" => !Off("EnableDefaultEmbeddedResourceItems"),
            _ => !Off("EnableDefaultNoneItems")
        };
    }

    /// <summary>
    /// The project's own line ending and the indentation of its top-level
    /// elements, so an added group looks like the rest of the file. Falls back
    /// to LF and two spaces for a project with no indented element.
    /// </summary>
    private static (string Newline, string Indent) LayoutOf(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = ProjectIndent.Match(text) is { Success: true } match ? match.Groups[1].Value : "  ";
        return (newline, indent);
    }

    /// <summary>
    /// Adds an item group holding one item on its own lines, just before the
    /// project's closing tag, in the project's own line ending and indentation.
    /// </summary>
    private static string AppendItemGroup(string projectText, string item)
    {
        var close = projectText.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
        if (close < 0) throw new InvalidDataException("The project file has no closing </Project> tag.");

        var (newline, indent) = LayoutOf(projectText);
        var group = $"{indent}<ItemGroup>{newline}{indent}{indent}{item}{newline}{indent}</ItemGroup>{newline}";

        // Inserted at the start of the closing tag's own line, so an indented
        // </Project> keeps its indentation after the group.
        var lineStart = close;
        while (lineStart > 0 && projectText[lineStart - 1] is ' ' or '\t') lineStart--;

        if (lineStart == 0 || projectText[lineStart - 1] == '\n')
            return projectText.Insert(lineStart, group);

        return projectText.Insert(close, newline + group);
    }

    /// <summary>Reads a project file's text, and whether it begins with a byte-order mark.</summary>
    private static string ReadText(string path, out bool byteOrderMark)
    {
        var bytes = File.ReadAllBytes(path);
        byteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var start = byteOrderMark ? 3 : 0;
        return new UTF8Encoding(false).GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Writes a project file's text as UTF-8, with the byte-order mark it had.</summary>
    private static void WriteText(string path, string text, bool byteOrderMark)
    {
        var body = new UTF8Encoding(false).GetBytes(text);
        if (!byteOrderMark)
        {
            File.WriteAllBytes(path, body);
            return;
        }

        var preamble = new UTF8Encoding(true).GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        File.WriteAllBytes(path, bytes);
    }
}
