namespace Basalt.Core.Commands;

/// <summary>Where a command belongs, for grouping in the settings.</summary>
public enum CommandCategory
{
    File, Edit, View, Navigate, Refactor, Build, Debug, Test, Git, Tools, Window, Help
}

/// <summary>
/// Something the IDE can be asked to do.
///
/// A command is named once and reached from everywhere: menu, toolbar,
/// keyboard, palette. Without this a shortcut lives in the XAML, a toolbar
/// button in code, and the two drift apart.
/// </summary>
public sealed record IdeCommand(
    string Id,
    string Title,
    CommandCategory Category)
{
    /// <summary>The shortcut it comes with, in the "Ctrl+S" form.</summary>
    public string? DefaultGesture { get; init; }

    /// <summary>A sentence saying what it does, shown in the palette.</summary>
    public string? Description { get; init; }

    /// <summary>Where the command is named for searching.</summary>
    public string SearchText => $"{Category} {Title} {Description}".ToLowerInvariant();
}

/// <summary>
/// Every command the IDE knows, and the keys assigned to them.
///
/// The registry holds what commands exist and what they are bound to; who
/// carries them out is the window's business. Keeping the two apart is what
/// lets the settings list every command without the settings knowing how any
/// of them work.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, IdeCommand> _commands = new(StringComparer.Ordinal);

    /// <summary>Gestures the user has changed, by command id.</summary>
    private readonly Dictionary<string, string?> _custom = new(StringComparer.Ordinal);

    public IReadOnlyList<IdeCommand> All =>
        [.. _commands.Values.OrderBy(c => c.Category).ThenBy(c => c.Title, StringComparer.Ordinal)];

    public IdeCommand? ById(string id) =>
        _commands.TryGetValue(id, out var command) ? command : null;

    public void Register(IdeCommand command) => _commands[command.Id] = command;

    public void RegisterAll(IEnumerable<IdeCommand> commands)
    {
        foreach (var command in commands) Register(command);
    }

    /// <summary>
    /// The shortcut a command answers to now.
    ///
    /// A user's choice wins over the one it came with; an explicit empty
    /// choice means they took the shortcut away.
    /// </summary>
    public string? GestureFor(string commandId) =>
        _custom.TryGetValue(commandId, out var custom)
            ? custom
            : ById(commandId)?.DefaultGesture;

    /// <summary>Assigns a shortcut, or removes it with null.</summary>
    public void Assign(string commandId, string? gesture) =>
        _custom[commandId] = string.IsNullOrWhiteSpace(gesture) ? null : gesture.Trim();

    /// <summary>Puts a command back to the shortcut it came with.</summary>
    public void ResetToDefault(string commandId) => _custom.Remove(commandId);

    /// <summary>Puts every command back to the shortcut it came with.</summary>
    public void ResetAll() => _custom.Clear();

    /// <summary>Whether a command's shortcut differs from the one it came with.</summary>
    public bool IsCustomised(string commandId) => _custom.ContainsKey(commandId);

    /// <summary>The changes worth writing to the settings.</summary>
    public IReadOnlyDictionary<string, string?> Customisations => _custom;

    /// <summary>Applies what was read from the settings.</summary>
    public void ApplyCustomisations(IReadOnlyDictionary<string, string?> customisations)
    {
        _custom.Clear();

        foreach (var (id, gesture) in customisations) _custom[id] = gesture;
    }

    /// <summary>
    /// The command a shortcut runs, or null when none does.
    ///
    /// Compared without regard to case or spacing, since a user typing a
    /// gesture by hand writes "ctrl+s" as readily as "Ctrl+S".
    /// </summary>
    public IdeCommand? ForGesture(string gesture)
    {
        var wanted = Normalise(gesture);

        return All.FirstOrDefault(c => Normalise(GestureFor(c.Id)) == wanted && wanted.Length > 0);
    }

    /// <summary>
    /// The commands already using a shortcut, apart from one.
    ///
    /// Assigning a key that is taken is not refused — the user may well mean
    /// it — but they are told what it will displace.
    /// </summary>
    public IReadOnlyList<IdeCommand> Conflicts(string gesture, string exceptCommandId)
    {
        var wanted = Normalise(gesture);

        if (wanted.Length == 0) return [];

        return [.. All
            .Where(c => c.Id != exceptCommandId)
            .Where(c => Normalise(GestureFor(c.Id)) == wanted)];
    }

    /// <summary>Commands whose name or category matches what was typed.</summary>
    public IReadOnlyList<IdeCommand> Search(string query)
    {
        var wanted = query.Trim().ToLowerInvariant();

        if (wanted.Length == 0) return All;

        return [.. All
            .Where(c => c.SearchText.Contains(wanted, StringComparison.Ordinal))
            .OrderByDescending(c => c.Title.ToLowerInvariant().StartsWith(wanted, StringComparison.Ordinal))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)];
    }

    private static string Normalise(string? gesture) =>
        gesture is null ? "" : gesture.Replace(" ", "").ToLowerInvariant();
}
