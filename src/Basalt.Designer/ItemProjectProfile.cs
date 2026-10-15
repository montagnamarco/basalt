using System.Xml;
using System.Xml.Linq;

namespace Basalt.Designer;

/// <summary>
/// What kind of project a folder belongs to, read from its project file, so
/// Add New Item can say which templates fit it.
///
/// Read from the file's text and nothing else: no restore, no MSBuild
/// evaluation. A project that cannot be read fits nothing but the plain items,
/// which is also what a folder with no project at all gets.
/// </summary>
public sealed record ItemProjectProfile
{
    /// <summary>A folder with no project, or one that cannot be read.</summary>
    public static ItemProjectProfile Unknown { get; } = new();

    /// <summary>A web project: the Web or Razor SDK, ASP.NET Core, or Basalt's Razor packages.</summary>
    public bool IsWeb { get; init; }

    /// <summary>
    /// A Windows Forms project, the way the WinForms project template says so:
    /// UseWindowsForms, a Windows target framework, or the System.Windows.Forms import.
    /// </summary>
    public bool IsWindowsForms { get; init; }

    public bool IsAvalonia { get; init; }

    public bool HasXUnit { get; init; }

    public bool HasNUnit { get; init; }

    public bool HasMSTest { get; init; }

    /// <summary>Whether the project meets what a template asks of it.</summary>
    public bool Satisfies(ItemRequirement requirement) => requirement switch
    {
        ItemRequirement.None => true,
        ItemRequirement.Web => IsWeb,
        ItemRequirement.WindowsForms => IsWindowsForms,
        ItemRequirement.Avalonia => IsAvalonia,
        ItemRequirement.XUnit => HasXUnit,
        ItemRequirement.NUnit => HasNUnit,
        ItemRequirement.MSTest => HasMSTest,
        _ => false
    };

    public static ItemProjectProfile FromProjectFile(string? projectPath)
    {
        if (projectPath is null || !File.Exists(projectPath)) return Unknown;

        try
        {
            return FromProjectXml(XDocument.Load(projectPath));
        }
        catch (XmlException)
        {
            return Unknown;
        }
        catch (IOException)
        {
            return Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return Unknown;
        }
    }

    public static ItemProjectProfile FromProjectXml(XDocument project)
    {
        var profile = new ItemProjectProfile();
        var sdk = (string?)project.Root?.Attribute("Sdk") ?? "";

        if (sdk.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || sdk.StartsWith("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase)
            || sdk.StartsWith("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
            profile = profile with { IsWeb = true };

        if (sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            profile = profile with { HasMSTest = true };

        foreach (var element in project.Descendants())
        {
            var include = (string?)element.Attribute("Include") ?? "";

            profile = element.Name.LocalName switch
            {
                "UseWindowsForms" when IsTrue(element.Value) => profile with { IsWindowsForms = true },
                "TargetFramework" or "TargetFrameworks" when
                    element.Value.Contains("-windows", StringComparison.OrdinalIgnoreCase)
                        => profile with { IsWindowsForms = true },
                "Import" when include.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
                    => profile with { IsWindowsForms = true },
                "FrameworkReference" when include.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)
                    => profile with { IsWeb = true },
                "PackageReference" or "Reference" => WithPackage(profile, include),
                _ => profile
            };
        }

        return profile;
    }

    private static ItemProjectProfile WithPackage(ItemProjectProfile profile, string package)
    {
        // Basalt's own packages are what make a .vbhtml or .vbrazor compile,
        // so a project that references them is a web project for these items.
        if (package.StartsWith("Basalt.Razor.Vb", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            profile = profile with { IsWeb = true };

        if (package.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            profile = profile with { IsAvalonia = true };

        // The framework itself, and only it: a runner, an analyzer or an
        // adapter runs or checks tests that something else defines.
        if (package.Equals("xunit", StringComparison.OrdinalIgnoreCase)
            || package.Equals("xunit.core", StringComparison.OrdinalIgnoreCase)
            || package.Equals("xunit.v3", StringComparison.OrdinalIgnoreCase)
            || package.Equals("xunit.v3.core", StringComparison.OrdinalIgnoreCase))
            profile = profile with { HasXUnit = true };

        if (package.Equals("NUnit", StringComparison.OrdinalIgnoreCase))
            profile = profile with { HasNUnit = true };

        if (package.Equals("MSTest", StringComparison.OrdinalIgnoreCase)
            || package.Equals("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase)
            || package.Equals("Microsoft.VisualStudio.TestPlatform.TestFramework", StringComparison.OrdinalIgnoreCase)
            || package.Equals("Microsoft.VisualStudio.TestTools.UnitTesting", StringComparison.OrdinalIgnoreCase))
            profile = profile with { HasMSTest = true };

        return profile;
    }

    private static bool IsTrue(string value) => string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
