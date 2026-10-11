using Avalonia.Input;
using Basalt.Core.Commands;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The registry's spelling of a shortcut, turned into the gesture a menu item
/// shows. The menus print what the key handler answers to, so these pin the
/// spellings the registry uses, including the symbol keys.
/// </summary>
public sealed class MenuShortcutTests
{
    [Theory]
    [InlineData("Ctrl+S", KeyModifiers.Control, Key.S, KeyModifiers.Control)]
    [InlineData("Ctrl+S", KeyModifiers.Meta, Key.S, KeyModifiers.Meta)]
    [InlineData("Ctrl+Shift+H", KeyModifiers.Control, Key.H, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Ctrl+Alt+M", KeyModifiers.Control, Key.M, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("Shift+F12", KeyModifiers.Control, Key.F12, KeyModifiers.Shift)]
    [InlineData("F12", KeyModifiers.Control, Key.F12, KeyModifiers.None)]
    [InlineData("Escape", KeyModifiers.Control, Key.Escape, KeyModifiers.None)]
    [InlineData("Ctrl+-", KeyModifiers.Control, Key.OemMinus, KeyModifiers.Control)]
    [InlineData("Ctrl+Shift+-", KeyModifiers.Control, Key.OemMinus, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Ctrl+,", KeyModifiers.Control, Key.OemComma, KeyModifiers.Control)]
    [InlineData("Ctrl+`", KeyModifiers.Control, Key.Oem3, KeyModifiers.Control)]
    [InlineData("Ctrl+/", KeyModifiers.Control, Key.Oem2, KeyModifiers.Control)]
    [InlineData("Ctrl+0", KeyModifiers.Control, Key.D0, KeyModifiers.Control)]
    [InlineData("Ctrl+9", KeyModifiers.Control, Key.D9, KeyModifiers.Control)]
    [InlineData("Ctrl++", KeyModifiers.Control, Key.OemPlus, KeyModifiers.Control)]
    public void AGestureReadsItsKeyAndModifiersWithTheCommandKeyFromThePlatform(
        string spelled, KeyModifiers commandModifier, Key key, KeyModifiers modifiers)
    {
        var gesture = MenuShortcuts.ToGesture(spelled, commandModifier);

        Assert.NotNull(gesture);
        Assert.Equal(key, gesture!.Key);
        Assert.Equal(modifiers, gesture.KeyModifiers);
    }

    [Theory]
    [InlineData("Ctrl+K, Ctrl+D")]
    [InlineData("Ctrl+M, Ctrl+O")]
    [InlineData("")]
    [InlineData(null)]
    public void AChordOrNoShortcutShowsNoGesture(string? spelled) =>
        Assert.Null(MenuShortcuts.ToGesture(spelled, KeyModifiers.Control));

    [Fact]
    public void EveryRegistryShortcutOfEverySchemeIsShownAsItIsSpelled()
    {
        // The menus and the key handler must agree on each shortcut: what a
        // gesture is read as, it must be spelled back as the registry spells it.
        var problems = new List<string>();

        foreach (var scheme in Enum.GetValues<KeyboardScheme>())
        {
            foreach (var command in IdeCommands.Definitions(scheme))
            {
                var spelled = command.DefaultGesture;

                if (spelled is null || spelled.Contains(", ", StringComparison.Ordinal)) continue;

                var gesture = MenuShortcuts.ToGesture(spelled, KeyModifiers.Control);
                if (gesture is null)
                {
                    problems.Add($"{scheme} {command.Id} {spelled}: no gesture");
                    continue;
                }

                var respelled = ShortcutRecorder.Describe(gesture.Key, gesture.KeyModifiers);
                if (respelled != spelled) problems.Add($"{scheme} {command.Id}: {spelled} came back as {respelled}");
            }
        }

        Assert.Empty(problems);
    }
}
