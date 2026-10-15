using Avalonia.Input;
using Basalt.Core.Commands;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The chord logic and the schemes' gestures, pressed as keys against the
/// registry itself. No window: <see cref="ShortcutChords"/> is a pure state
/// machine, and the window is tested in <see cref="EditorShortcutTests"/>.
/// </summary>
public sealed class EditorShortcutSchemeTests
{
    private static KeyGesture Gesture(string spelling) =>
        MenuShortcuts.ToGesture(spelling, KeyModifiers.Control)
        ?? throw new InvalidOperationException($"'{spelling}' is not a key the test can press.");

    private static ChordStep Press(ShortcutChords chords, CommandRegistry registry, string spelling)
    {
        var key = Gesture(spelling);
        return chords.Press(registry, key.Key, key.KeyModifiers);
    }

    /// <summary>Every chord the scheme defines completes its own command when its two halves are pressed.</summary>
    [Theory]
    [InlineData(KeyboardScheme.Basalt)]
    [InlineData(KeyboardScheme.VisualStudio)]
    [InlineData(KeyboardScheme.VisualStudioCode)]
    public void EveryChordOfTheSchemeCompletesItsOwnCommand(KeyboardScheme scheme)
    {
        var registry = IdeCommands.CreateRegistry(scheme);
        var chords = IdeCommands.Definitions(scheme)
            .Where(command => ShortcutChords.Split(command.DefaultGesture) is not null)
            .ToList();

        Assert.NotEmpty(chords);

        foreach (var command in chords)
        {
            var (first, second) = ShortcutChords.Split(command.DefaultGesture)!.Value;
            var state = new ShortcutChords();

            Assert.Equal(ChordOutcome.Waiting, Press(state, registry, first).Outcome);

            var step = Press(state, registry, second);
            Assert.True(step.Outcome == ChordOutcome.Completed && step.Command?.Id == command.Id,
                $"{scheme}: {command.DefaultGesture} did not run {command.Id}; it gave {step.Outcome}.");
        }
    }

    [Fact]
    public void AChordIsForgottenOnceItHasRun()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        var chords = new ShortcutChords();

        Press(chords, registry, "Ctrl+K");
        Assert.Equal(ChordOutcome.Completed, Press(chords, registry, "Ctrl+D").Outcome);

        // Ctrl+D alone is not a first half, so it is an ordinary shortcut again.
        Assert.Equal(ChordOutcome.NotAChord, Press(chords, registry, "Ctrl+D").Outcome);
        Assert.Null(chords.Pending);
    }

    [Fact]
    public void EscapeCancelsAWaitingChord()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        var chords = new ShortcutChords();

        Assert.Equal(ChordOutcome.Waiting, Press(chords, registry, "Ctrl+K").Outcome);
        Assert.Equal("Ctrl+K", chords.Pending);

        var step = chords.Press(registry, Key.Escape, KeyModifiers.None);

        Assert.Equal(ChordOutcome.Cancelled, step.Outcome);
        Assert.Null(chords.Pending);
    }

    /// <summary>A key that completes no chord cancels the chord and runs nothing.</summary>
    [Fact]
    public void AKeyThatCompletesNoChordCancelsItAndRunsNothing()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        var chords = new ShortcutChords();

        Press(chords, registry, "Ctrl+K");
        var step = Press(chords, registry, "Ctrl+Z");

        Assert.Equal(ChordOutcome.Cancelled, step.Outcome);
        Assert.Null(step.Command);
        Assert.Null(chords.Pending);
    }

    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+Z")]
    [InlineData("F12")]
    public void AShortcutThatIsNoChordsFirstHalfIsNotAChord(string spelling)
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        var chords = new ShortcutChords();

        Assert.Equal(ChordOutcome.NotAChord, Press(chords, registry, spelling).Outcome);
        Assert.Null(chords.Pending);
    }

    /// <summary>Settings > Keyboard decides the chords: the Visual Studio Code scheme has no Format Document chord.</summary>
    [Fact]
    public void TheSchemeDecidesWhatAChordCompletesTo()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.VisualStudioCode);
        var chords = new ShortcutChords();

        Assert.Equal(ChordOutcome.Waiting, Press(chords, registry, "Ctrl+K").Outcome);
        Assert.Equal(ChordOutcome.Cancelled, Press(chords, registry, "Ctrl+D").Outcome);
    }

    /// <summary>A chord the user assigned is the one that runs, and one the user removed is gone.</summary>
    [Fact]
    public void AUserAssignedChordRunsAndARemovedOneDoesNot()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        registry.Assign(IdeCommands.EditFormatDocument, "Ctrl+K, Ctrl+F");
        var chords = new ShortcutChords();

        Press(chords, registry, "Ctrl+K");
        Assert.Equal(IdeCommands.EditFormatDocument, Press(chords, registry, "Ctrl+F").Command?.Id);

        registry.Assign(IdeCommands.EditFormatDocument, null);

        Press(chords, registry, "Ctrl+K");
        Assert.Equal(ChordOutcome.Cancelled, Press(chords, registry, "Ctrl+D").Outcome);
    }

    [Theory]
    [InlineData("Ctrl+K, Ctrl+D", "Ctrl+K", "Ctrl+D")]
    [InlineData("Ctrl+M, Ctrl+M", "Ctrl+M", "Ctrl+M")]
    public void SplitReadsTheTwoHalvesOfAChord(string chord, string first, string second) =>
        Assert.Equal((first, second), ShortcutChords.Split(chord));

    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+,")]
    [InlineData("F12")]
    public void SplitFindsNoHalvesInASingleKey(string gesture) =>
        Assert.Null(ShortcutChords.Split(gesture));

    [Fact]
    public void SplitFindsNoHalvesWhenThereIsNoGesture() =>
        Assert.Null(ShortcutChords.Split(null));

    /// <summary>Redo answers to Ctrl+Y and Ctrl+Shift+Z under both schemes, each where the scheme leaves it unbound.</summary>
    [Fact]
    public void RedoAnswersToTheGestureTheSchemeDoesNotUse()
    {
        var basalt = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        var visualStudio = IdeCommands.CreateRegistry(KeyboardScheme.VisualStudio);

        Assert.Equal(IdeCommands.EditRedo, ShortcutChords.AlternateFor(basalt, "Ctrl+Y")?.Id);
        Assert.Null(ShortcutChords.AlternateFor(basalt, "Ctrl+Shift+Z"));

        Assert.Equal(IdeCommands.EditRedo, ShortcutChords.AlternateFor(visualStudio, "Ctrl+Shift+Z")?.Id);
        Assert.Null(ShortcutChords.AlternateFor(visualStudio, "Ctrl+Y"));
    }

    /// <summary>A key the user has bound to something else keeps that binding; Ctrl+Y is not redo then.</summary>
    [Fact]
    public void AUserBindingWinsOverTheRedoAlternate()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);
        registry.Assign(IdeCommands.NavigateGoToFile, "Ctrl+Y");

        Assert.Null(ShortcutChords.AlternateFor(registry, "Ctrl+Y"));
    }

    /// <summary>Duplicate Line is Ctrl+D under Basalt and Visual Studio; Visual Studio Code keeps Ctrl+D for selecting the next match.</summary>
    [Theory]
    [InlineData(KeyboardScheme.Basalt, "Ctrl+D")]
    [InlineData(KeyboardScheme.VisualStudio, "Ctrl+D")]
    [InlineData(KeyboardScheme.VisualStudioCode, null)]
    public void DuplicateLineHasTheSchemesGesture(KeyboardScheme scheme, string? gesture) =>
        Assert.Equal(gesture, IdeCommands.CreateRegistry(scheme).GestureFor(IdeCommands.EditDuplicateLine));
}
