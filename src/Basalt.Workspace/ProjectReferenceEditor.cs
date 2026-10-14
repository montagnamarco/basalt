using System.Text;
using System.Text.RegularExpressions;

namespace Basalt.Workspace;

/// <summary>
/// Reads and edits the <c>&lt;ProjectReference&gt;</c>, <c>&lt;Reference&gt;</c>
/// and <c>&lt;PackageReference&gt;</c> items of a project file's text, for the
/// References dialog (Project &gt; References…).
///
/// Unlike <see cref="Basalt.Workspace.Projects.VbProjectFile"/>, which parses
/// the file into an <see cref="System.Xml.Linq.XDocument"/> and writes the
/// whole tree back out, this edits the text directly: a new item is a line
/// inserted next to its siblings, a removed one is the lines it occupied taken
/// out, and every other line — comments, blank lines, whatever formatting the
/// rest of the file happens to have — passes through untouched. A references
/// dialog is opened far more casually than the properties window, often just
/// to glance at what is already there, so a comment or an unusual indent
/// surviving the round trip matters more here.
/// </summary>
public static class ProjectReferenceEditor
{
    /// <summary>A &lt;Reference&gt; item: an assembly by name, optionally with a HintPath to its file.</summary>
    public readonly record struct AssemblyReference(string Include, string? HintPath);

    /// <summary>A &lt;PackageReference&gt; item: a NuGet package id and the version pinned.</summary>
    public readonly record struct PackageEntry(string Id, string Version);

    private static readonly Regex Attribute = new(
        "(?<name>\\w+)\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex ChildElement = new(
        @"<(?<name>\w+)>(?<value>[^<]*)</\k<name>>", RegexOptions.Compiled);

    // Reading

    public static IReadOnlyList<string> GetProjectReferences(string projectXml) =>
        [.. FindItems(SplitLines(projectXml).Lines, "ProjectReference")
            .Select(ItemKey)
            .Where(path => path.Length > 0)];

    public static IReadOnlyList<AssemblyReference> GetAssemblyReferences(string projectXml) =>
        [.. FindItems(SplitLines(projectXml).Lines, "Reference")
            .Where(HasKey)
            .Select(item => new AssemblyReference(
                ItemKey(item),
                item.Children.GetValueOrDefault("HintPath")))];

    public static IReadOnlyList<PackageEntry> GetPackageReferences(string projectXml) =>
        [.. FindItems(SplitLines(projectXml).Lines, "PackageReference")
            .Where(HasKey)
            .Select(item => new PackageEntry(
                ItemKey(item),
                item.Attributes.GetValueOrDefault("Version", "")))];

    /// <summary>
    /// True when <paramref name="projectXml"/>'s own MSBuild properties turn
    /// on central package management (the rest of the signal — a
    /// <c>Directory.Packages.props</c> file above the project — is a
    /// filesystem question the caller, which knows the project's path,
    /// answers itself).
    /// </summary>
    public static bool DeclaresCentralPackageManagement(string projectXml) =>
        Regex.IsMatch(projectXml,
            @"<ManagePackageVersionsCentrally>\s*true\s*</ManagePackageVersionsCentrally>",
            RegexOptions.IgnoreCase);

    /// <summary>
    /// An item's identity: its Include, or — for an item that only adds or
    /// overrides metadata on one declared elsewhere, MSBuild's own idiom —
    /// its Update. Visual Studio's Reference Manager shows either kind the
    /// same way, and so does this.
    /// </summary>
    private static string ItemKey(ItemBlock item) =>
        item.Attributes.GetValueOrDefault("Include")
            ?? item.Attributes.GetValueOrDefault("Update", "");

    private static bool HasKey(ItemBlock item) =>
        item.Attributes.ContainsKey("Include") || item.Attributes.ContainsKey("Update");

    // Writing

    /// <summary>
    /// Adds a project reference, as a path relative to the project file —
    /// written with backslashes regardless of platform, the way Visual
    /// Studio itself writes a ProjectReference's Include. Already present
    /// (by that same normalised path): unchanged, so adding it twice is a
    /// no-op rather than a duplicate line.
    /// </summary>
    public static string AddProjectReference(string projectXml, string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        if (GetProjectReferences(projectXml).Any(existing => PathsEqual(existing, normalized)))
            return projectXml;

        return AddSelfClosingItem(projectXml, "ProjectReference",
            [("Include", normalized)]);
    }

    public static string RemoveProjectReference(string projectXml, string relativePath) =>
        RemoveItem(projectXml, "ProjectReference", NormalizePath(relativePath), PathsEqual);

    /// <summary>
    /// Adds a reference to an assembly by name, with a HintPath to its file
    /// when one is given (a bare framework assembly name needs none).
    /// Already present (by Include or Update, ignoring case and any
    /// HintPath already on record — the way MSBuild itself matches an
    /// assembly name): unchanged.
    /// </summary>
    public static string AddAssemblyReference(string projectXml, string include, string? hintPath)
    {
        if (GetAssemblyReferences(projectXml).Any(existing =>
                string.Equals(existing.Include, include, StringComparison.OrdinalIgnoreCase)))
            return projectXml;

        return hintPath is null
            ? AddSelfClosingItem(projectXml, "Reference", [("Include", include)])
            : AddItemWithChild(projectXml, "Reference", include, "HintPath", NormalizePath(hintPath));
    }

    public static string RemoveAssemblyReference(string projectXml, string include) =>
        RemoveItem(projectXml, "Reference", include,
            (name, wanted) => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a package reference, or updates the version of one already
    /// there — by id, case-insensitively, the way NuGet itself treats a
    /// package id. Under central package management
    /// (<paramref name="centralPackageManagement"/>) a new item is written
    /// without a Version — <c>Directory.Packages.props</c> owns that — and
    /// one already there is never touched: the caller is expected to tell
    /// the user the version lives elsewhere rather than let this silently
    /// do nothing.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The version is blank and this project's version is not managed centrally.
    /// Writing the reference without one would float it to the newest
    /// package, so the caller asks for a version first.
    /// </exception>
    public static string AddPackageReference(
        string projectXml, string id, string version, bool centralPackageManagement = false)
    {
        if (!centralPackageManagement && string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A package reference needs a version to pin it.", nameof(version));

        var (lines, newLine) = SplitLines(projectXml);
        var existing = FindItems(lines, "PackageReference")
            .FirstOrDefault(item => string.Equals(ItemKey(item), id, StringComparison.OrdinalIgnoreCase));

        if (existing.Attributes is null)
            return centralPackageManagement
                ? AddSelfClosingItem(projectXml, "PackageReference", [("Include", id)])
                : AddSelfClosingItem(projectXml, "PackageReference", [("Include", id), ("Version", version)]);

        if (centralPackageManagement) return projectXml;

        if (string.Equals(existing.Attributes.GetValueOrDefault("Version", ""), version, StringComparison.Ordinal))
            return projectXml;

        lines[existing.StartLine] = SetAttribute(lines[existing.StartLine], "Version", version);
        return JoinLines(lines, newLine);
    }

    public static string RemovePackageReference(string projectXml, string id) =>
        RemoveItem(projectXml, "PackageReference", id,
            (name, wanted) => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));

    /// <summary>A path as Visual Studio writes one into a project file: backslash-separated.</summary>
    public static string NormalizePath(string path) => path.Replace('/', '\\');

    /// <summary>
    /// Case-insensitively, the way MSBuild itself compares a path when
    /// deciding whether an item is already there — "..\Lib\Lib.vbproj" and
    /// "..\LIB\Lib.vbproj" are the same reference even though the file
    /// system underneath might disagree.
    /// </summary>
    private static bool PathsEqual(string a, string b) =>
        string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

    // Line-oriented item model

    private readonly record struct ItemBlock(
        int StartLine, int EndLine, bool SelfClosing,
        IReadOnlyDictionary<string, string> Attributes,
        IReadOnlyDictionary<string, string> Children);

    private static List<ItemBlock> FindItems(string[] lines, string tag)
    {
        var results = new List<ItemBlock>();

        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], $@"<{tag}\b(?<attrs>[^>]*?)(?<self>/)?>");
            if (!match.Success) continue;

            var attributes = ParseAttributes(match.Groups["attrs"].Value);

            if (match.Groups["self"].Success)
            {
                results.Add(new ItemBlock(i, i, true, attributes, new Dictionary<string, string>()));
                continue;
            }

            var children = new Dictionary<string, string>();
            var closeTag = $"</{tag}>";
            var end = i;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Contains(closeTag, StringComparison.Ordinal)) { end = j; break; }

                var child = ChildElement.Match(lines[j]);
                if (child.Success) children[child.Groups["name"].Value] = UnescapeXml(child.Groups["value"].Value);
            }

            results.Add(new ItemBlock(i, end, false, attributes, children));
            i = end;
        }

        return results;
    }

    private static Dictionary<string, string> ParseAttributes(string attributeText)
    {
        var attributes = new Dictionary<string, string>();
        foreach (Match match in Attribute.Matches(attributeText))
            attributes[match.Groups["name"].Value] = UnescapeXml(match.Groups["value"].Value);
        return attributes;
    }

    /// <summary>
    /// The five predefined XML entities, decoded in an order that does not
    /// double-decode: &amp;amp; must be the last substitution, or an
    /// attribute that legitimately contains the text "&amp;lt;" would come
    /// back as "&lt;" instead.
    /// </summary>
    private static string UnescapeXml(string value) =>
        value
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&#39;", "'")
            .Replace("&amp;", "&");

    /// <summary>
    /// The inverse of <see cref="UnescapeXml"/>, for a value about to be
    /// written into an attribute or an element's text — everything this
    /// editor ever inserts (a path, an assembly name, a package id or
    /// version) is untrusted text that might carry one of these characters,
    /// and an unescaped "&amp;" or "&lt;" would make the file invalid XML
    /// rather than just an odd-looking reference.
    /// </summary>
    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

    private static string SetAttribute(string line, string name, string value)
    {
        var pattern = new Regex(Regex.Escape(name) + "\\s*=\\s*\"[^\"]*\"");
        return pattern.IsMatch(line)
            ? pattern.Replace(line, $"{name}=\"{EscapeXml(value)}\"", 1)
            : line;
    }

    // Insertion

    private static string AddSelfClosingItem(
        string projectXml, string tag, (string Name, string Value)[] attributes)
    {
        var attributeText = string.Join(" ", attributes.Select(a => $"{a.Name}=\"{EscapeXml(a.Value)}\""));
        return InsertItemLines(projectXml, tag, [$"<{tag} {attributeText} />"]);
    }

    private static string AddItemWithChild(
        string projectXml, string tag, string include, string childName, string childValue)
    {
        return InsertItemLines(projectXml, tag,
        [
            $"<{tag} Include=\"{EscapeXml(include)}\">",
            $"<{childName}>{EscapeXml(childValue)}</{childName}>",
            $"</{tag}>"
        ], indentChildrenBy: 2);
    }

    /// <summary>
    /// Inserts the given lines (already relative to each other, unindented)
    /// as a new item: next to its siblings in the first ItemGroup that
    /// already holds one of the same kind, matching that group's own
    /// indentation, or in a new ItemGroup created near the end of the
    /// project when there is no such group yet.
    /// </summary>
    private static string InsertItemLines(
        string projectXml, string tag, string[] itemLines, int indentChildrenBy = 0)
    {
        var (lines, newLine) = SplitLines(projectXml);

        var group = FindItemGroupContaining(lines, tag);
        if (group is { } found)
        {
            var indent = IndentOfFirstItem(lines, found.StartLine, found.EndLine)
                ?? IndentOf(lines[found.StartLine]) + "  ";

            var inserted = itemLines.Select((text, index) =>
                indent + (index == 0 ? text : new string(' ', indentChildrenBy) + text)).ToArray();

            var result = new List<string>(lines[..found.EndLine]);
            result.AddRange(inserted);
            result.AddRange(lines[found.EndLine..]);
            return JoinLines(result.ToArray(), newLine);
        }

        return InsertNewItemGroup(lines, newLine, itemLines, indentChildrenBy);
    }

    private static (int StartLine, int EndLine)? FindItemGroupContaining(string[] lines, string tag)
    {
        var groups = FindItems(lines, "ItemGroup");
        foreach (var group in groups)
        {
            for (var i = group.StartLine; i <= group.EndLine; i++)
                if (lines[i].Contains($"<{tag}", StringComparison.Ordinal))
                    return (group.StartLine, group.EndLine);
        }

        return null;
    }

    private static string? IndentOfFirstItem(string[] lines, int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '<') continue;
            return IndentOf(lines[i]);
        }

        return null;
    }

    private static string IndentOf(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] is ' ' or '\t') count++;
        return line[..count];
    }

    private static string InsertNewItemGroup(
        string[] lines, string newLine, string[] itemLines, int indentChildrenBy)
    {
        var projectCloseIndex = Array.FindLastIndex(lines, l => l.Contains("</Project>", StringComparison.Ordinal));
        if (projectCloseIndex < 0) projectCloseIndex = lines.Length - 1;

        // Matches the indentation an existing top-level group already uses
        // (PropertyGroup, almost always); two spaces when the file has none
        // to copy from, the convention every Basalt-written project follows.
        var baseIndent = FindItems(lines, "PropertyGroup").Select(g => IndentOf(lines[g.StartLine]))
            .FirstOrDefault() ?? "  ";
        var itemIndent = baseIndent + "  ";

        var block = new List<string> { baseIndent + "<ItemGroup>" };
        block.AddRange(itemLines.Select((text, index) =>
            itemIndent + (index == 0 ? text : new string(' ', indentChildrenBy) + text)));
        block.Add(baseIndent + "</ItemGroup>");

        var result = new List<string>(lines[..projectCloseIndex]);

        // A blank line separates groups in every project this IDE writes;
        // not doubled when one is already there (the file's own last line
        // before </Project> is often already blank).
        if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");

        result.AddRange(block);
        result.AddRange(lines[projectCloseIndex..]);
        return JoinLines(result.ToArray(), newLine);
    }

    // Removal

    private static string RemoveItem(
        string projectXml, string tag, string key, Func<string, string, bool> matches)
    {
        var (lines, newLine) = SplitLines(projectXml);
        var items = FindItems(lines, tag);

        // An Include item and an Update item can both name the same
        // reference (the second usually adding metadata to the first);
        // removing drops the Include form — the one that actually brings
        // the reference in — rather than the metadata-only one, so the
        // reference is gone rather than left half-declared.
        var target = items
            .Where(item => matches(ItemKey(item), key))
            .OrderByDescending(item => item.Attributes.ContainsKey("Include"))
            .FirstOrDefault();

        if (target.Attributes is null) return projectXml;

        var result = new List<string>(lines[..target.StartLine]);
        result.AddRange(lines[(target.EndLine + 1)..]);
        return JoinLines(result.ToArray(), newLine);
    }

    // Line splitting, preserving the file's own line-ending style exactly.

    private static (string[] Lines, string NewLine) SplitLines(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return (text.Replace("\r\n", "\n").Split('\n'), newLine);
    }

    private static string JoinLines(string[] lines, string newLine)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) builder.Append(newLine);
            builder.Append(lines[i]);
        }

        return builder.ToString();
    }
}
