using System.Text;
using System.Text.RegularExpressions;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Describes the opened solution to the assistant: its name and what each
/// project is.
///
/// The text is the same from one question to the next unless the solution
/// changes, which is what lets the prompt cache keep it.
/// </summary>
public static class AiProjectContext
{
    private const int MaxProjects = 40;

    public static string Describe(string? solutionPath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return "";

        var text = new StringBuilder();
        var name = solutionPath is { Length: > 0 }
            ? Path.GetFileName(solutionPath)
            : Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));

        text.Append("The open solution is ").Append(name)
            .Append(". File paths in tools are relative to its folder.\n");

        var projects = FindProjects(rootDirectory).Take(MaxProjects).ToList();

        if (projects.Count == 0)
        {
            text.Append("No project files were found in the folder.");
            return text.ToString().TrimEnd();
        }

        text.Append("Projects:\n");

        foreach (var project in projects)
            text.Append("- ").Append(Path.GetRelativePath(rootDirectory, project).Replace('\\', '/'))
                .Append(": ").Append(Kind(project)).Append('\n');

        return text.ToString().TrimEnd();
    }

    private static IEnumerable<string> FindProjects(string root)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && found.Count < MaxProjects)
        {
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    var extension = Path.GetExtension(file);

                    if (extension is ".vbproj" or ".csproj" or ".fsproj") found.Add(file);
                }

                foreach (var directory in Directory.EnumerateDirectories(current))
                    if (!AiWorkspaceSandbox.ExcludedDirectories.Contains(
                            Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                        pending.Push(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read has no projects to describe.
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>The language, the target and what kind of application a project file declares.</summary>
    internal static string Kind(string projectPath)
    {
        var language = Path.GetExtension(projectPath) switch
        {
            ".vbproj" => "Visual Basic",
            ".csproj" => "C#",
            _ => "F#"
        };

        string xml;

        try
        {
            xml = File.ReadAllText(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return language;
        }

        var traits = new List<string> { language };

        var target = Regex.Match(xml, @"<TargetFrameworks?>\s*([^<]+?)\s*</TargetFrameworks?>");
        if (target.Success) traits.Add(target.Groups[1].Value);

        var sdk = Regex.Match(xml, @"Sdk=""([^""]+)""").Groups[1].Value;

        if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase)) traits.Add("ASP.NET Core");
        else if (sdk.Contains("Razor", StringComparison.OrdinalIgnoreCase)) traits.Add("Razor class library");

        if (Regex.IsMatch(xml, @"<UseWindowsForms>\s*true", RegexOptions.IgnoreCase)
            || xml.Contains("Basalt.Forms", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Basalt.WinForms", StringComparison.OrdinalIgnoreCase))
            traits.Add("WinForms");

        if (Regex.IsMatch(xml, @"<UseWPF>\s*true", RegexOptions.IgnoreCase)) traits.Add("WPF");

        if (xml.Contains("Avalonia", StringComparison.OrdinalIgnoreCase)) traits.Add("Avalonia");

        if (xml.Contains("Basalt.Razor.Vb", StringComparison.OrdinalIgnoreCase)) traits.Add("Razor for Visual Basic");

        var output = Regex.Match(xml, @"<OutputType>\s*([^<]+?)\s*</OutputType>").Groups[1].Value;
        if (output.Length > 0) traits.Add(output.ToLowerInvariant() switch
        {
            "exe" => "executable",
            "winexe" => "windowed executable",
            "library" => "library",
            var other => other
        });

        var directory = Path.GetDirectoryName(projectPath)!;

        try
        {
            if (Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(f => Path.GetExtension(f) is ".vbhtml" or ".vbrazor" or ".vbpage")
                && !traits.Contains("Razor for Visual Basic"))
                traits.Add("Razor for Visual Basic");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return string.Join(", ", traits);
    }
}
