using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basalt.Workspace.Projects;

/// <summary>
/// How a project is started when run or debugged.
///
/// These live in the project's launchSettings.json (see
/// <see cref="LaunchSettingsLocation"/>) rather than in the project file, so
/// the properties window reads and writes them separately.
/// </summary>
public sealed class LaunchSettings
{
    private readonly JsonObject _root;

    /// <summary>
    /// The older file this one replaces, when the settings were read from it:
    /// Saving writes the new file and then removes the old one.
    /// </summary>
    private readonly string? _migratedFrom;

    private LaunchSettings(JsonObject root, string path, string profileName, string? migratedFrom = null)
    {
        _root = root;
        Path = path;
        ProfileName = profileName;
        _migratedFrom = migratedFrom;
    }

    public string Path { get; }

    /// <summary>Profile being edited; a project may define several.</summary>
    public string ProfileName { get; }

    /// <summary>
    /// Loads the settings for a project, creating an empty set when the file
    /// does not exist yet.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="LaunchSettingsLocation.PathOf"/>. A Visual Basic
    /// project the older IDE wrote into Properties/ is read from there until
    /// it is saved, and then moved.
    /// </remarks>
    public static LaunchSettings Load(string projectPath)
    {
        var path = LaunchSettingsLocation.PathOf(projectPath);
        var legacy = LaunchSettingsLocation.LegacyPathOf(projectPath);

        var source = File.Exists(path) ? path
            : legacy is not null && File.Exists(legacy) ? legacy
            : null;
        var migratedFrom = source is not null && source == legacy ? legacy : null;

        var projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath);

        if (source is null)
            return new LaunchSettings(NewDocument(projectName), path, projectName);

        try
        {
            var parsed = JsonNode.Parse(File.ReadAllText(source)) as JsonObject
                         ?? NewDocument(projectName);

            // The first profile that runs the project is the one the IDE runs, matching what "dotnet run" does
            // without a named profile: it skips IIS Express and Executable profiles. A file with none keeps its
            // first profile, so that the properties window still has one to edit.
            var profiles = parsed["profiles"] as JsonObject;
            var name = profiles?.FirstOrDefault(p => p.Value is JsonObject o && o["commandName"]?.ToString() == "Project").Key
                       ?? profiles?.FirstOrDefault(p => p.Value is JsonObject).Key
                       ?? projectName;

            return new LaunchSettings(parsed, path, name, migratedFrom);
        }
        catch (JsonException)
        {
            // A hand-edited file that no longer parses must not stop the IDE
            // from opening the project.
            return new LaunchSettings(NewDocument(projectName), path, projectName);
        }
    }

    private static JsonObject NewDocument(string projectName) => new()
    {
        ["profiles"] = new JsonObject
        {
            [projectName] = new JsonObject { ["commandName"] = "Project" }
        }
    };

    private JsonObject Profile
    {
        get
        {
            var profiles = _root["profiles"] as JsonObject;

            if (profiles is null)
            {
                profiles = new JsonObject();
                _root["profiles"] = profiles;
            }

            if (profiles[ProfileName] is not JsonObject profile)
            {
                profile = new JsonObject { ["commandName"] = "Project" };
                profiles[ProfileName] = profile;
            }

            return profile;
        }
    }

    /// <summary>What the profile launches: "Project" for the project itself, which is the only kind "dotnet run" applies.</summary>
    public string? CommandName => Profile["commandName"]?.ToString();

    /// <summary>Arguments passed to the program.</summary>
    public string? CommandLineArguments
    {
        get => Profile["commandLineArgs"]?.GetValue<string>();
        set => SetOrRemove("commandLineArgs", value);
    }

    /// <summary>Directory the program starts in.</summary>
    public string? WorkingDirectory
    {
        get => Profile["workingDirectory"]?.GetValue<string>();
        set => SetOrRemove("workingDirectory", value);
    }

    /// <summary>Address a web project listens on.</summary>
    public string? ApplicationUrl
    {
        get => Profile["applicationUrl"]?.GetValue<string>();
        set => SetOrRemove("applicationUrl", value);
    }

    public bool LaunchBrowser
    {
        get => Profile["launchBrowser"]?.GetValue<bool>() ?? false;
        set => Profile["launchBrowser"] = value;
    }

    /// <summary>Variables set for the program's process.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables
    {
        get
        {
            if (Profile["environmentVariables"] is not JsonObject variables)
                return new Dictionary<string, string>();

            return variables
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!.ToString());
        }
    }

    public void SetEnvironmentVariable(string name, string? value)
    {
        if (Profile["environmentVariables"] is not JsonObject variables)
        {
            if (value is null) return;

            variables = new JsonObject();
            Profile["environmentVariables"] = variables;
        }

        if (value is null) variables.Remove(name);
        else variables[name] = value;
    }

    private void SetOrRemove(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) Profile.Remove(key);
        else Profile[key] = value;
    }

    /// <summary>
    /// Writes the file. When the settings were migrated from the older
    /// location, that file is removed once the new one is written, and its
    /// folder too when nothing else is left in it.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null) Directory.CreateDirectory(directory);

        var json = _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(Path, json + Environment.NewLine, ct).ConfigureAwait(false);

        if (_migratedFrom is null || !File.Exists(_migratedFrom)) return;

        File.Delete(_migratedFrom);
        var legacyFolder = System.IO.Path.GetDirectoryName(_migratedFrom)!;
        if (Directory.Exists(legacyFolder) && !Directory.EnumerateFileSystemEntries(legacyFolder).Any())
            Directory.Delete(legacyFolder);
    }

    public string ToJson() =>
        _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
