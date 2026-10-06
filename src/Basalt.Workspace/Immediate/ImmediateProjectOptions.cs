using System.Xml.Linq;

namespace Basalt.Workspace.Immediate;

/// <summary>
/// The language settings of the startup project that a line typed in the
/// Immediate window is compiled with: namespace imports, root namespace and
/// the Option statements.
///
/// Read straight from the project file rather than asked of MSBuild: the
/// answer is wanted in well under a second, and an unreadable or unusual file
/// simply yields the defaults, which is the right fallback for a convenience.
/// </summary>
public sealed record ImmediateProjectOptions
{
    /// <summary>Namespaces every Visual Basic project imports without being asked.</summary>
    internal static readonly string[] DefaultImports =
    [
        "Microsoft.VisualBasic", "System", "System.Collections", "System.Collections.Generic",
        "System.Diagnostics", "System.IO", "System.Linq"
    ];

    public IReadOnlyList<string> Imports { get; init; } = [];
    public string? RootNamespace { get; init; }
    public bool? OptionStrict { get; init; }
    public bool? OptionExplicit { get; init; }
    public bool? OptionInfer { get; init; }
    public bool? OptionCompareText { get; init; }

    public static ImmediateProjectOptions Default { get; } = new();

    /// <summary>Reads the options of a project file, or the defaults when it cannot be read.</summary>
    public static ImmediateProjectOptions Read(string? projectPath)
    {
        if (projectPath is null || !File.Exists(projectPath)) return Default;

        try
        {
            var project = XDocument.Load(projectPath).Root;

            if (project is null) return Default;

            // Local names only: old-style projects carry an MSBuild namespace.
            string? Property(string name) => project
                .Descendants()
                .Where(e => e.Name.LocalName == name)
                .Select(e => e.Value.Trim())
                .LastOrDefault(v => v.Length > 0);

            bool? Switch(string name) => Property(name)?.ToLowerInvariant() switch
            {
                "on" or "true" => true,
                "off" or "false" => false,
                _ => null
            };

            var imports = project
                .Descendants()
                .Where(e => e.Name.LocalName == "Import" && e.Attribute("Include") is not null)
                .Select(e => e.Attribute("Include")!.Value.Trim())
                .Where(name => name.Length > 0 && !name.Contains('$'))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new ImmediateProjectOptions
            {
                Imports = imports,
                RootNamespace = Property("RootNamespace"),
                OptionStrict = Switch("OptionStrict"),
                OptionExplicit = Switch("OptionExplicit"),
                OptionInfer = Switch("OptionInfer"),
                OptionCompareText = Property("OptionCompare")?.Equals("Text", StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Xml.XmlException)
        {
            return Default;
        }
    }
}
