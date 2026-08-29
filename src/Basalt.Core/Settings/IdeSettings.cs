using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basalt.Core.Settings;

/// <summary>How dense the interface should be.</summary>
public enum InterfaceDensity { Comfortable, Compact }

/// <summary>
/// Which theme the interface uses.
///
/// The first three keep the names they had, so a settings file written before
/// the others existed still reads back as the theme it named.
/// </summary>
public enum AppTheme
{
    Light,
    Dark,
    System,
    VisualStudioBlue,
    HighContrastLight,
    HighContrastDark,
    SolarizedLight,
    SolarizedDark
}

/// <summary>Settings for the text editor.</summary>
public sealed class EditorSettings
{
    public string FontFamily { get; set; } = "Menlo,Consolas,DejaVu Sans Mono,monospace";
    public double FontSize { get; set; } = 13;
    public int IndentationSize { get; set; } = 4;
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool WordWrap { get; set; }

    /// <summary>Whether Visual Basic conventions are applied while typing.</summary>
    public bool FormatWhileTyping { get; set; } = true;

    public bool FormatOnSave { get; set; } = true;
    public bool AutoCloseBrackets { get; set; } = true;
    public bool ShowWhitespace { get; set; }

    /// <summary>Whether a completion list appears without being asked for.</summary>
    public bool CompleteAutomatically { get; set; } = true;
}

/// <summary>
/// Which buttons the toolbar shows, and in what order.
///
/// Empty means the ones it comes with: a user who has never chosen should get
/// the default even after buttons are added to a later version, which a
/// stored copy of the old list would prevent.
/// </summary>
public sealed class ToolbarSettings
{
    public List<string> Buttons { get; set; } = [];

    /// <summary>Whether the build configuration and startup project are shown.</summary>
    public bool ShowChoosers { get; set; } = true;
}

/// <summary>Settings for how the interface looks.</summary>
public sealed class AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public InterfaceDensity Density { get; set; } = InterfaceDensity.Comfortable;

    /// <summary>The interface language, as a culture name such as "en".</summary>
    public string Language { get; set; } = "en";
}

/// <summary>Settings for the terminals.</summary>
public sealed class TerminalSettings
{
    /// <summary>The shell to start; empty means the user's login shell.</summary>
    public string Shell { get; set; } = "";

    public double FontSize { get; set; } = 12;

    /// <summary>How many lines of output are kept for scrolling back.</summary>
    public int ScrollbackLines { get; set; } = 5000;
}

/// <summary>Settings for debugging.</summary>
public sealed class DebugSettings
{
    /// <summary>Where netcoredbg is; empty means look for it.</summary>
    public string AdapterPath { get; set; } = "";

    /// <summary>Whether to stop as soon as the program starts.</summary>
    public bool StopAtEntry { get; set; }

    /// <summary>Whether to step through code without debug symbols.</summary>
    public bool StepIntoExternalCode { get; set; }
}

/// <summary>What a build produces, and from which project.</summary>
public sealed class BuildSettings
{
    /// <summary>"Debug" or "Release": what Build and Run use.</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>The project that runs; empty means the one found in the solution.</summary>
    public string StartupProject { get; set; } = "";
}

/// <summary>
/// Which shortcuts the IDE answers to.
///
/// Only what the user changed is kept, not a copy of every default: a stored
/// copy would never follow a later change to the defaults, and the user would
/// be stuck with yesterday's keys without knowing why.
/// </summary>
public sealed class KeyboardSettings
{
    /// <summary>The scheme the defaults come from, as its name.</summary>
    public string Scheme { get; set; } = "Basalt";

    /// <summary>
    /// The shortcuts the user assigned, by command id.
    ///
    /// An entry with an empty value means the user took the shortcut away,
    /// which is different from never having changed it.
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Which assistant to use, and how.
///
/// The API key is deliberately absent: it lives in the platform keychain, not
/// in a settings file a user may copy between machines or paste into a bug
/// report.
/// </summary>
public sealed class AiSettings
{
    /// <summary>Which assistant, as its name; empty means none is chosen.</summary>
    public string Vendor { get; set; } = "";

    /// <summary>The model, or empty for the assistant's default.</summary>
    public string Model { get; set; } = "";

    /// <summary>Whether the open file is sent along with the question.</summary>
    public bool SendFileContext { get; set; } = true;

    /// <summary>Whether the current errors are sent along with the question.</summary>
    public bool SendErrors { get; set; } = true;
}

/// <summary>
/// Everything the IDE remembers between sessions.
///
/// One object written as one file: settings are read at startup and saved when
/// changed, and a single document keeps them consistent with each other.
/// Per-language settings are kept as loose values so a language added later
/// needs no change here.
/// </summary>
public sealed class IdeSettings
{
    public EditorSettings Editor { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public TerminalSettings Terminal { get; set; } = new();
    public DebugSettings Debug { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
    public BuildSettings Build { get; set; } = new();
    public ToolbarSettings Toolbar { get; set; } = new();
    public RecentSettings Recent { get; set; } = new();

    /// <summary>What was open in each solution when it was last closed.</summary>
    public SessionSettings Session { get; set; } = new();

    /// <summary>
    /// Settings contributed by a language, keyed by language id.
    ///
    /// Loose rather than typed so that adding a language does not mean
    /// changing this class, which is the point of the extensible architecture.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new();

    /// <summary>Reads a language's setting, or the fallback when it is unset.</summary>
    public string GetLanguageSetting(string languageId, string key, string fallback = "") =>
        Languages.TryGetValue(languageId, out var settings)
        && settings.TryGetValue(key, out var value)
            ? value
            : fallback;

    public void SetLanguageSetting(string languageId, string key, string value)
    {
        if (!Languages.TryGetValue(languageId, out var settings))
        {
            settings = [];
            Languages[languageId] = settings;
        }

        settings[key] = value;
    }
}

/// <summary>
/// Reads and writes the settings file.
///
/// A file that cannot be read is replaced by the defaults rather than stopping
/// the IDE: settings are a convenience, and losing them must not cost the user
/// their editor.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SettingsStore(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>
    /// Where the settings live, following each platform's convention.
    ///
    /// BASALT_SETTINGS overrides it. The tests set it: without that they
    /// write to the settings of whoever is running them — the test suite
    /// filling a developer's own list of recent solutions is not a failure
    /// any assertion would catch.
    /// </summary>
    public static string DefaultPath
    {
        get
        {
            if (Environment.GetEnvironmentVariable("BASALT_SETTINGS") is { Length: > 0 } set)
                return set;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(home, ".basalt", "settings.json");
        }
    }

    /// <summary>The file these settings are read from and written to.</summary>
    public string FilePath => _path;

    /// <summary>Raised after settings are loaded or saved.</summary>
    public event EventHandler<IdeSettings>? Changed;

    public IdeSettings Load()
    {
        IdeSettings settings;

        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<IdeSettings>(File.ReadAllText(_path), Format) ?? new()
                : new IdeSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt file should not stop the IDE from opening.
            settings = new IdeSettings();
        }

        Changed?.Invoke(this, settings);
        return settings;
    }

    /// <summary>Writes the settings, reporting whether it worked.</summary>
    public bool Save(IdeSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Format));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        Changed?.Invoke(this, settings);
        return true;
    }
}
