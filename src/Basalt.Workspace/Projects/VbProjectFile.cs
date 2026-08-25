using System.Xml.Linq;

namespace Basalt.Workspace.Projects;

/// <summary>
/// A project file, edited in place.
///
/// The XML tree is modified rather than regenerated, so comments, formatting
/// and any hand-written markup the IDE does not understand survive a round trip
/// through the properties window — the same reason the XAML designer works this
/// way.
/// </summary>
public sealed class VbProjectFile
{
    private readonly XDocument _xml;

    private VbProjectFile(XDocument xml, string path)
    {
        _xml = xml;
        Path = path;
    }

    public string Path { get; }

    public XElement Root => _xml.Root
        ?? throw new InvalidOperationException("Project file has no root element.");

    public static VbProjectFile Load(string path) =>
        new(XDocument.Parse(File.ReadAllText(path), LoadOptions.PreserveWhitespace), path);

    public static VbProjectFile Parse(string xml, string path = "in-memory.vbproj") =>
        new(XDocument.Parse(xml, LoadOptions.PreserveWhitespace), path);

    public async Task SaveAsync(CancellationToken ct = default) =>
        await File.WriteAllTextAsync(Path, ToXml(), ct).ConfigureAwait(false);

    public string ToXml() => _xml.Declaration is null
        ? _xml.ToString(SaveOptions.DisableFormatting)
        : _xml.Declaration + Environment.NewLine + _xml.ToString(SaveOptions.DisableFormatting);

    /// <summary>
    /// Value of a property, or null when the project does not set it.
    ///
    /// The last occurrence wins, matching how MSBuild evaluates a file where a
    /// property is set more than once.
    /// </summary>
    public string? GetProperty(string name) =>
        Root.Elements("PropertyGroup")
            .SelectMany(g => g.Elements(name))
            .LastOrDefault()
            ?.Value;

    /// <summary>
    /// Sets a property, creating it if needed.
    ///
    /// A null or empty value removes the property rather than writing an empty
    /// element, except where emptiness is meaningful — see
    /// <see cref="SetPropertyAllowingEmpty"/>.
    /// </summary>
    public void SetProperty(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            RemoveProperty(name);
            return;
        }

        SetPropertyAllowingEmpty(name, value);
    }

    /// <summary>
    /// Sets a property, keeping an empty value.
    ///
    /// `RootNamespace` is the case that matters: in Visual Basic an empty root
    /// namespace is a deliberate setting, not an absent one, because otherwise
    /// it is prepended to every declared namespace.
    /// </summary>
    public void SetPropertyAllowingEmpty(string name, string value)
    {
        var existing = Root.Elements("PropertyGroup")
            .SelectMany(g => g.Elements(name))
            .LastOrDefault();

        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        var group = Root.Elements("PropertyGroup").FirstOrDefault();

        if (group is null)
        {
            group = new XElement("PropertyGroup");
            Root.AddFirst(group);
        }

        group.Add(new XElement(name, value));
    }

    public void RemoveProperty(string name)
    {
        foreach (var element in Root.Elements("PropertyGroup")
                     .SelectMany(g => g.Elements(name))
                     .ToList())
            element.Remove();
    }

    /// <summary>Package references, as id and version.</summary>
    public IReadOnlyList<(string Id, string? Version)> GetPackageReferences() =>
        Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("PackageReference"))
            .Select(e => (
                Id: e.Attribute("Include")?.Value ?? "",
                Version: e.Attribute("Version")?.Value))
            .Where(r => r.Id.Length > 0)
            .ToList();

    public void AddPackageReference(string id, string version)
    {
        var existing = Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("PackageReference"))
            .FirstOrDefault(e => e.Attribute("Include")?.Value == id);

        if (existing is not null)
        {
            existing.SetAttributeValue("Version", version);
            return;
        }

        // Packages go together in one group, so the file stays readable.
        var group = Root.Elements("ItemGroup")
            .FirstOrDefault(g => g.Elements("PackageReference").Any());

        if (group is null)
        {
            group = new XElement("ItemGroup");
            Root.Add(group);
        }

        group.Add(new XElement("PackageReference",
            new XAttribute("Include", id),
            new XAttribute("Version", version)));
    }

    public bool RemovePackageReference(string id)
    {
        var existing = Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("PackageReference"))
            .FirstOrDefault(e => e.Attribute("Include")?.Value == id);

        if (existing is null) return false;

        existing.Remove();
        return true;
    }

    /// <summary>Referenced projects, as paths relative to this file.</summary>
    public IReadOnlyList<string> GetProjectReferences() =>
        Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("ProjectReference"))
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .Where(p => p.Length > 0)
            .ToList();

    public void AddProjectReference(string relativePath)
    {
        var already = Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("ProjectReference"))
            .Any(e => e.Attribute("Include")?.Value == relativePath);

        if (already) return;

        var group = Root.Elements("ItemGroup")
            .FirstOrDefault(g => g.Elements("ProjectReference").Any());

        if (group is null)
        {
            group = new XElement("ItemGroup");
            Root.Add(group);
        }

        group.Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
    }

    public bool RemoveProjectReference(string relativePath)
    {
        var existing = Root.Elements("ItemGroup")
            .SelectMany(g => g.Elements("ProjectReference"))
            .FirstOrDefault(e => e.Attribute("Include")?.Value == relativePath);

        if (existing is null) return false;

        existing.Remove();
        return true;
    }
}
