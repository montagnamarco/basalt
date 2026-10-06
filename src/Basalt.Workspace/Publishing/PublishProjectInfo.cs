using System.Xml.Linq;

namespace Basalt.Workspace.Publishing;

/// <summary>
/// What "Make executable" needs to know about a project, read from its XML.
///
/// The same judgement the compatibility layer makes: the project file alone,
/// not an evaluation. A property that comes only from a Directory.Build.props
/// is not seen (the bundle then falls back to the defaults below).
/// </summary>
public sealed record PublishProjectInfo(
    string ProjectPath,
    string AssemblyName,
    string Version,
    bool IsGraphical,
    string? ApplicationIcon,
    string? RootNamespace = null,
    string? Company = null,
    bool AssemblyNameUnresolved = false)
{
    public string ProjectDirectory => Path.GetDirectoryName(Path.GetFullPath(ProjectPath)) ?? ".";

    public static PublishProjectInfo Read(string projectPath)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        string? assemblyName = null, outputType = null, version = null, icon = null, rootNamespace = null, company = null;
        var assemblyNameUnresolved = false;
        var windowsForms = false;
        var avalonia = false;

        try
        {
            var document = XDocument.Load(projectPath);

            foreach (var group in document.Descendants().Where(e => e.Name.LocalName == "PropertyGroup"))
            {
                foreach (var property in group.Elements())
                {
                    var value = property.Value.Trim();
                    if (value.Length == 0) continue;

                    // A conditional or unexpanded name cannot be known without
                    // evaluating the project, and a wrong guess names a file
                    // that is not there: the project's file name stands in.
                    if (property.Name.LocalName == "AssemblyName")
                    {
                        if (value.Contains("$(", StringComparison.Ordinal)
                            || property.Attribute("Condition") is not null
                            || group.Attribute("Condition") is not null)
                            assemblyNameUnresolved = true;
                        else
                            assemblyName = value;
                        continue;
                    }

                    switch (property.Name.LocalName)
                    {
                        case "RootNamespace": rootNamespace = value; break;
                        case "Company": company = value; break;
                        case "OutputType": outputType = value; break;
                        case "ApplicationIcon": icon = value; break;
                        case "UseWindowsForms": windowsForms = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "Version": version = value; break;
                        case "VersionPrefix": version ??= value; break;
                    }
                }
            }

            avalonia = document.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Any(e => ((string?)e.Attribute("Include"))?.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // The publish itself reports an unreadable project; the defaults
            // keep this from adding a second, vaguer error.
        }

        var graphical = string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
            || windowsForms
            || avalonia;

        return new PublishProjectInfo(projectPath, assemblyName ?? name, version ?? "1.0.0", graphical, icon,
            rootNamespace, company, assemblyName is null && assemblyNameUnresolved);
    }
}
