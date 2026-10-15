using System.Text.Json;
using Basalt.Designer;

namespace Basalt.Shell;

/// <summary>
/// The category Add New Item was last left on, kept in a small file beside the
/// IDE settings (so BASALT_SETTINGS moves it with them, and a test with a
/// settings file of its own has a file of its own). A file that is missing or
/// unreadable is the default category, never an error: forgetting the last
/// category costs one click, failing to open Add New Item costs the feature.
/// </summary>
public static class AddItemMemory
{
    private sealed record Stored(string? Category);

    /// <summary><c>add-item.json</c> beside settings.json, and <c>name.add-item.json</c> beside any other name.</summary>
    public static string PathBeside(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath) ?? "";
        var name = Path.GetFileName(settingsPath);

        return Path.Combine(directory, name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            ? "add-item.json"
            : Path.GetFileNameWithoutExtension(name) + ".add-item.json");
    }

    public static ItemCategory Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { Category: { } name }
                && Enum.TryParse<ItemCategory>(name, out var category)
                && Enum.IsDefined(category))
                return category;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Falls through to the default.
        }

        return ItemCategory.Code;
    }

    public static void Save(string path, ItemCategory category)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(new Stored(category.ToString())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept; the dialog opens on Code next time.
        }
    }
}
