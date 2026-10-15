using System.Text.Json;
using Basalt.Data.Sessions;

namespace Basalt.Shell.Database;

/// <summary>Where a diagram's boxes were left, and how it was panned and zoomed.</summary>
/// <param name="Tables">Each table's top-left corner in diagram coordinates, by the table's name as <see cref="DbObjectName.ToString"/> writes it.</param>
public sealed record DiagramLayout(
    IReadOnlyDictionary<string, DiagramPoint> Tables,
    double Zoom = 1,
    double PanX = 0,
    double PanY = 0);

/// <summary>
/// The layout of each connection's diagram, kept in a file beside the IDE settings.
/// </summary>
/// <remarks>
/// A file of its own, like the query history: a box dragged is a write, and the settings file is written whole and
/// would race with the connection list. It sits beside the settings file, so BASALT_SETTINGS moves it with them
/// and a test with a settings file of its own has a layout file of its own. The file is read and written on the
/// pool through one gate; a file that cannot be read is an empty layout, and one that cannot be written is
/// a layout that was not kept, never an error in the diagram. Written to a uniquely named temporary file and moved
/// into place, so a crash mid-write cannot leave half a file that would lose every connection's layout. A file that
/// cannot be parsed is kept as <c>.bak</c> and a new one started; one that cannot be read is never overwritten, because
/// the other connections' layouts in it are not ours to wipe.
/// </remarks>
public sealed class DiagramLayoutStore
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DiagramLayoutStore(string path) => _path = path;

    /// <summary>
    /// The layout file that sits beside a settings file: <c>database-diagrams.json</c> beside settings.json, and
    /// <c>name.database-diagrams.json</c> beside any other name.
    /// </summary>
    public static string PathBeside(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath) ?? "";
        var name = Path.GetFileName(settingsPath);

        return Path.Combine(directory, name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            ? "database-diagrams.json"
            : Path.GetFileNameWithoutExtension(name) + ".database-diagrams.json");
    }

    public string FilePath => _path;

    /// <summary>The saved layout of a connection, or null when it has none.</summary>
    public Task<DiagramLayout?> GetAsync(string connectionId) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            return Read() is { } all && all.TryGetValue(connectionId, out var stored) ? stored.ToLayout() : null;
        }
        finally
        {
            _gate.Release();
        }
    });

    /// <summary>Keeps a connection's layout, replacing the one it had.</summary>
    public Task SaveAsync(string connectionId, DiagramLayout layout) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Read() is not { } all) return;

            all[connectionId] = StoredLayout.From(layout);
            Write(all);
        }
        finally
        {
            _gate.Release();
        }
    });

    /// <summary>Forgets a connection's layout, so the next diagram of it starts from the automatic one.</summary>
    public Task ForgetAsync(string connectionId) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Read() is { } all && all.Remove(connectionId)) Write(all);
        }
        finally
        {
            _gate.Release();
        }
    });

    /// <summary>
    /// The layouts in the file. A file that is not valid JSON is moved aside as <c>.bak</c>, so the next save starts a
    /// new file without destroying what could still be read by hand. A file that cannot be read at all (locked, no
    /// access) returns null: it is left alone, and the save that wanted to add to it is skipped rather than replace it.
    /// </summary>
    private Dictionary<string, StoredLayout>? Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            return JsonSerializer.Deserialize<Dictionary<string, StoredLayout>>(File.ReadAllText(_path), Format) ?? [];
        }
        catch (JsonException)
        {
            KeepAside();

            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void KeepAside()
    {
        try
        {
            var aside = _path + ".bak";

            // An earlier .bak is somebody's last good copy: it is not overwritten by a newer bad file.
            if (File.Exists(aside)) aside = $"{_path}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak";

            File.Move(_path, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If it cannot be moved it will be replaced by the next write, which is the best that can be done.
        }
    }

    private void Write(Dictionary<string, StoredLayout> all)
    {
        // A name of its own, so two IDE windows writing at once do not write into each other's temporary file.
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";

        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

            File.WriteAllText(temporary, JsonSerializer.Serialize(all, Format));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A layout that could not be kept is drawn again from the automatic one next time.
            try { File.Delete(temporary); } catch (Exception) { }
        }
    }

    /// <summary>The file's shape: plain numbers, so it reads and diffs by hand.</summary>
    private sealed class StoredLayout
    {
        public double Zoom { get; set; } = 1;

        public double PanX { get; set; }

        public double PanY { get; set; }

        public Dictionary<string, double[]> Tables { get; set; } = [];

        public static StoredLayout From(DiagramLayout layout) => new()
        {
            Zoom = layout.Zoom,
            PanX = layout.PanX,
            PanY = layout.PanY,
            Tables = layout.Tables.ToDictionary(t => t.Key, t => new[] { t.Value.X, t.Value.Y }),
        };

        public DiagramLayout ToLayout() => new(
            Tables.Where(t => t.Value is { Length: 2 }).ToDictionary(t => t.Key, t => new DiagramPoint(t.Value[0], t.Value[1])),
            double.IsFinite(Zoom) && Zoom > 0 ? Zoom : 1,
            double.IsFinite(PanX) ? PanX : 0,
            double.IsFinite(PanY) ? PanY : 0);
    }
}
