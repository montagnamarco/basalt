using Avalonia.Input;
using Basalt.Core.Commands;
using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>What one key press did to a chord in progress.</summary>
internal enum ChordOutcome
{
    /// <summary>No chord is involved: the key goes on to the ordinary shortcuts.</summary>
    NotAChord,

    /// <summary>The key was the first half of a chord; the second key is awaited.</summary>
    Waiting,

    /// <summary>The key was the second half of a chord, and <see cref="ChordStep.Command"/> runs.</summary>
    Completed,

    /// <summary>The chord was abandoned; nothing runs.</summary>
    Cancelled
}

/// <summary>The result of <see cref="ShortcutChords.Press"/>.</summary>
internal readonly record struct ChordStep(
    ChordOutcome Outcome,
    string? FirstHalf = null,
    IdeCommand? Command = null);

/// <summary>
/// Two-key chords as Visual Studio has them: Ctrl+K, Ctrl+D is Format
/// Document, and Ctrl+K is held until the second key arrives.
///
/// Which chords exist is read from the registry on every key press, so the
/// keyboard scheme in force and any shortcut the user assigned decide what a
/// first half starts. Nothing here names a command.
/// </summary>
internal sealed class ShortcutChords
{
    private const string Separator = ", ";

    // Redo under both schemes: Basalt names Ctrl+Shift+Z in its menu, Visual
    // Studio names Ctrl+Y, and each user knows the other too. Used only where
    // the scheme leaves the gesture unbound, so a binding the user made wins.
    private static readonly (string Gesture, string CommandId)[] AlternateGestures =
    [
        ("Ctrl+Y", IdeCommands.EditRedo),
        ("Ctrl+Shift+Z", IdeCommands.EditRedo)
    ];

    /// <summary>The first half waiting for its second key, as the registry spells it, or null.</summary>
    public string? Pending { get; private set; }

    /// <summary>
    /// Takes one key press.
    ///
    /// Escape always cancels a chord in progress, whatever a chord might have
    /// bound to it. Any other key that completes no chord also cancels it, and
    /// is not then taken for a shortcut of its own: the user meant the chord.
    /// </summary>
    public ChordStep Press(CommandRegistry commands, Key key, KeyModifiers modifiers)
    {
        var gesture = ShortcutRecorder.Describe(key, modifiers);

        if (Pending is { } firstHalf)
        {
            Pending = null;

            if (key == Key.Escape) return new ChordStep(ChordOutcome.Cancelled);

            return commands.ForGesture(Join(firstHalf, gesture)) is { } command
                ? new ChordStep(ChordOutcome.Completed, firstHalf, command)
                : new ChordStep(ChordOutcome.Cancelled, firstHalf);
        }

        if (!IsFirstHalf(commands, gesture)) return new ChordStep(ChordOutcome.NotAChord);

        Pending = gesture;

        return new ChordStep(ChordOutcome.Waiting, gesture);
    }

    /// <summary>Abandons a chord in progress, for instance because a text box has the keyboard.</summary>
    public void Cancel() => Pending = null;

    /// <summary>
    /// The command an alternate spelling runs, when the scheme leaves the gesture
    /// unbound. Null when something is bound to it, or when it is not an alternate.
    /// </summary>
    public static IdeCommand? AlternateFor(CommandRegistry commands, string gesture)
    {
        if (commands.ForGesture(gesture) is not null) return null;

        foreach (var (spelling, commandId) in AlternateGestures)
        {
            if (Same(spelling, gesture)) return commands.ById(commandId);
        }

        return null;
    }

    /// <summary>
    /// The two halves of a chord spelling such as "Ctrl+K, Ctrl+D", or null for
    /// a single key. The halves are separated by comma and space, the spelling
    /// <see cref="MenuShortcuts"/> reads too, so that "Ctrl+," (Settings) is a
    /// single key.
    /// </summary>
    public static (string First, string Second)? Split(string? gesture)
    {
        if (gesture is null) return null;

        var index = gesture.IndexOf(Separator, StringComparison.Ordinal);

        return index < 0
            ? null
            : (gesture[..index].Trim(), gesture[(index + Separator.Length)..].Trim());
    }

    private static bool IsFirstHalf(CommandRegistry commands, string gesture)
    {
        foreach (var command in commands.All)
        {
            if (Split(commands.GestureFor(command.Id)) is { } halves && Same(halves.First, gesture))
                return true;
        }

        return false;
    }

    private static string Join(string firstHalf, string secondHalf) => firstHalf + Separator + secondHalf;

    /// <summary>Compares two spellings the way the registry does: without regard to case or spacing.</summary>
    private static bool Same(string left, string right) =>
        string.Equals(left.Replace(" ", ""), right.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}
