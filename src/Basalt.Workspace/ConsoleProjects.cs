using Basalt.Workspace.Projects;

namespace Basalt.Workspace;

/// <summary>
/// Tells a console program apart from a windowed one, which decides where
/// Run Without Debugging puts its output.
///
/// A console program runs in a terminal tab, where the person can type to it.
/// Anything with a window keeps the redirected run, whose output goes to the
/// Output panel: a window has no terminal to type into.
/// </summary>
public static class ConsoleProjects
{
    /// <summary>
    /// The packages that make a project an Avalonia application. A project that
    /// references one draws its own window, whatever its output type says.
    /// </summary>
    private static readonly string[] AvaloniaPackages = ["Avalonia", "Avalonia.Desktop"];

    /// <summary>
    /// True when the project builds a console program: an executable on the
    /// plain .NET SDK, without Windows Forms, WPF or Avalonia.
    ///
    /// A project that cannot be read is not a console program, so it keeps the
    /// redirected run it always had rather than starting in a terminal on a guess.
    /// </summary>
    public static bool IsConsole(string projectPath)
    {
        VbProjectFile project;
        try
        {
            project = VbProjectFile.Load(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        // The SDK decides the output type's defaults and the targets it pulls in.
        // Web, Razor and Blazor projects are executables too, but they serve
        // pages, so they keep the run they always had.
        var sdk = project.Root.Attribute("Sdk")?.Value.Trim();
        if (!string.Equals(sdk, "Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase)) return false;

        if (!string.Equals(project.GetProperty("OutputType")?.Trim(), "Exe", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsTrue(project.GetProperty("UseWindowsForms")) || IsTrue(project.GetProperty("UseWPF")))
            return false;

        // NuGet package ids are case-insensitive, so the comparison is too.
        return !project.GetPackageReferences()
            .Any(reference => AvaloniaPackages.Contains(reference.Id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>MSBuild reads "true" in any case as true, and anything else as false.</summary>
    private static bool IsTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
