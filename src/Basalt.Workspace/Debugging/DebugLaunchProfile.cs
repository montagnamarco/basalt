using System.Text;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Workspace.Projects;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Turns a project's launch profile (<c>My Project/launchSettings.json</c>) into what the debugger starts the
/// program with, so F5 runs it as Ctrl+F5 does: "dotnet run" applies the profile itself, while the debugger
/// starts the built assembly and has to be told.
/// </summary>
public static class DebugLaunchProfile
{
    /// <summary>The variable "dotnet run" sets from a profile's applicationUrl, which web hosts listen on.</summary>
    internal const string UrlsVariable = "ASPNETCORE_URLS";

    /// <summary>
    /// The options the project's first profile gives: its arguments split as a command line, its variables, and its
    /// working directory (relative to the project's folder). Reads the file: call it off the interface thread.
    /// </summary>
    /// <param name="projectPath">The project file.</param>
    /// <param name="defaultDirectory">The working directory when the profile names none.</param>
    public static LaunchOptions For(string projectPath, string? defaultDirectory)
    {
        var settings = LaunchSettings.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

        // A profile that does not run the project (IIS Express, Executable) is none of the debugger's business, as it
        // is none of "dotnet run"'s.
        if (settings.CommandName is { Length: > 0 } command && command != "Project")
            return LaunchOptions.In(defaultDirectory);

        try
        {
            var directory = settings.WorkingDirectory is { Length: > 0 } named
                ? Path.GetFullPath(named, projectDirectory)
                : defaultDirectory;

            var environment = new Dictionary<string, string>(StringComparer.Ordinal);

            // As "dotnet run" does: a profile's address becomes the host's URLs unless a variable already says.
            if (settings.ApplicationUrl is { Length: > 0 } url) environment[UrlsVariable] = url;

            foreach (var (name, value) in settings.EnvironmentVariables) environment[name] = value;

            return new LaunchOptions(directory, SplitCommandLine(settings.CommandLineArguments), environment);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // A value of the wrong kind (commandLineArgs given as a list): the exception of the JSON reader names no
            // file and no setting.
            throw new InvalidDataException(Localizer.Get(StringKeys.DebugLaunchProfileInvalid, settings.Path, ex.Message), ex);
        }
    }

    /// <summary>
    /// Splits a command line into arguments the way Windows does and "dotnet run" reads a profile's
    /// <c>commandLineArgs</c>: spaces separate, double quotes group and are dropped, a backslash before a quote
    /// escapes it, and backslashes elsewhere are kept as they are.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var arguments = new List<string>();

        if (string.IsNullOrWhiteSpace(commandLine)) return arguments;

        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (c == '\\')
            {
                var slashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\') { slashes++; i++; }

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    // 2n backslashes then a quote: n backslashes and the quote opens or closes a group;
                    // 2n+1: n backslashes and a literal quote.
                    current.Append('\\', slashes / 2);

                    if (slashes % 2 == 1) current.Append('"');
                    else inQuotes = !inQuotes;
                }
                else
                {
                    current.Append('\\', slashes);
                    i--;
                }

                started = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (started) arguments.Add(current.ToString());

                current.Clear();
                started = false;
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }

        if (started) arguments.Add(current.ToString());

        return arguments;
    }
}
