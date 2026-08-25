using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Commands;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Keys that do something, checked by pressing them.
///
/// A survey found that no test anywhere raised a keyboard event: every key
/// handler in the IDE was written and never exercised. These press the keys
/// for real, which is the only way to know a handler is wired to what it
/// claims.
/// </summary>
public sealed class KeyboardHandlerTests
{
    /// <summary>Presses a key on a control, as the user would.</summary>
    private static bool Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = target
        };

        target.RaiseEvent(args);

        return args.Handled;
    }

    private static CommandPalette OpenPalette()
    {
        var palette = new CommandPalette(IdeCommands.CreateRegistry(KeyboardScheme.Basalt));

        palette.Show();

        return palette;
    }

    [AvaloniaFact]
    public void TheDownArrowMovesThroughTheMatches()
    {
        var palette = OpenPalette();

        var first = palette.SelectedIndex;

        Assert.True(Press(palette.QueryBoxForTests, Key.Down), "Down was not handled.");
        Assert.NotEqual(first, palette.SelectedIndex);
    }

    [AvaloniaFact]
    public void TheUpArrowMovesBack()
    {
        var palette = OpenPalette();

        Press(palette.QueryBoxForTests, Key.Down);
        var second = palette.SelectedIndex;

        Assert.True(Press(palette.QueryBoxForTests, Key.Up), "Up was not handled.");
        Assert.NotEqual(second, palette.SelectedIndex);
    }

    [AvaloniaFact]
    public void EscapeClosesThePalette()
    {
        var palette = OpenPalette();

        Assert.True(Press(palette.QueryBoxForTests, Key.Escape), "Escape was not handled.");
    }

    [AvaloniaFact]
    public void EnterChoosesWhatIsSelected()
    {
        var palette = OpenPalette();

        Assert.True(Press(palette.QueryBoxForTests, Key.Enter), "Enter was not handled.");
        Assert.NotNull(palette.Chosen);
    }

    [AvaloniaFact]
    public void AKeyThatMeansNothingIsLeftAlone()
    {
        // A handler that swallowed every key would stop the user typing.
        var palette = OpenPalette();

        Assert.False(Press(palette.QueryBoxForTests, Key.A),
            "A letter was swallowed by the key handler.");
    }

    // Go to symbol

    [AvaloniaFact]
    public void TheArrowKeysMoveThroughTheSymbolList()
    {
        var dialog = new GoToSymbolDialog(
        [
            new SymbolChoice("First", Basalt.Extensibility.SymbolKind.Method, "a.vb", 1),
            new SymbolChoice("Second", Basalt.Extensibility.SymbolKind.Method, "b.vb", 2)
        ], "Go to Symbol");

        dialog.Show();

        var first = dialog.SelectedIndexForTests;

        Assert.True(Press(dialog.QueryBoxForTests, Key.Down), "Down was not handled.");
        Assert.NotEqual(first, dialog.SelectedIndexForTests);
    }

    [AvaloniaFact]
    public void EscapeClosesTheSymbolDialog()
    {
        var dialog = new GoToSymbolDialog([], "Go to Symbol");

        dialog.Show();

        Assert.True(Press(dialog.QueryBoxForTests, Key.Escape), "Escape was not handled.");
    }

    // Watch panel

    [AvaloniaFact]
    public void EnterAddsAWatchExpression()
    {
        var panel = new Basalt.Shell.Controls.WatchPanel();

        panel.InputForTests.Text = "counter";

        Assert.True(Press(panel.InputForTests, Key.Enter), "Enter was not handled.");
        Assert.Contains(panel.Entries, e => e.Expression == "counter");
    }

    [AvaloniaFact]
    public void EnterOnAnEmptyBoxAddsNothing()
    {
        var panel = new Basalt.Shell.Controls.WatchPanel();

        panel.InputForTests.Text = "";
        Press(panel.InputForTests, Key.Enter);

        Assert.Empty(panel.Entries);
    }

    [AvaloniaFact]
    public void DeleteRemovesTheSelectedWatch()
    {
        var panel = new Basalt.Shell.Controls.WatchPanel();

        panel.InputForTests.Text = "counter";
        Press(panel.InputForTests, Key.Enter);

        panel.ListForTests.SelectedIndex = 0;

        Assert.True(Press(panel.ListForTests, Key.Delete), "Delete was not handled.");
        Assert.Empty(panel.Entries);
    }

    [AvaloniaFact]
    public void ALetterInTheWatchListIsLeftAlone()
    {
        var panel = new Basalt.Shell.Controls.WatchPanel();

        panel.InputForTests.Text = "counter";
        Press(panel.InputForTests, Key.Enter);
        panel.ListForTests.SelectedIndex = 0;

        Assert.False(Press(panel.ListForTests, Key.A), "A letter was swallowed.");
        Assert.Single(panel.Entries);
    }
}
