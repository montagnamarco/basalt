using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public class MenuTests
{
    [AvaloniaFact]
    public void IlMenuVieneInstallatoDoveLaPiattaformaSeLoAspetta()
    {
        using var host = new TestWindow();
        var window = host.Window;

        var windowMenu = window.FindControl<Menu>("WindowMenu")!;

        if (IdeMenu.UsesSystemMenuBar)
        {
            // On macOS the menu is exported to the system menu bar: the one
            // inside the window must not appear.
            Assert.NotNull(NativeMenu.GetMenu(window));
            Assert.False(windowMenu.IsVisible);
        }
        else
        {
            Assert.NotNull(windowMenu.ItemsSource);
            Assert.NotEmpty(windowMenu.ItemsSource!.Cast<object>());
        }
    }

    [AvaloniaFact]
    public void IlMenuNativoContieneLeVociPrincipali()
    {
        var entries = new List<MenuEntry>
        {
            new("File", Children: [new("Nuova soluzione…"), MenuEntry.Separator, new("Esci")]),
            new("Compila", Children: [new("Compila soluzione")])
        };

        var menu = IdeMenu.BuildNative(entries);

        Assert.Equal(2, menu.Items.Count);

        var file = Assert.IsType<NativeMenuItem>(menu.Items[0]);
        Assert.Equal("File", file.Header);
        Assert.NotNull(file.Menu);
        Assert.Equal(3, file.Menu!.Items.Count);
        Assert.IsType<NativeMenuItemSeparator>(file.Menu.Items[1]);
    }

    [AvaloniaFact]
    public void IlMenuInFinestraRispecchiaLaStessaStruttura()
    {
        var entries = new List<MenuEntry>
        {
            new("File", Children: [new("Nuova soluzione…"), new("Esci")])
        };

        var items = IdeMenu.BuildManaged(entries);

        var file = Assert.Single(items);
        Assert.Equal("File", file.Header);
        Assert.Equal(2, file.ItemsSource!.Cast<object>().Count());
    }

    [AvaloniaFact]
    public void LeScorciatoieUsanoIlModificatoreDellaPiattaforma()
    {
        var entry = new MenuEntry("Salva",
            Gesture: new KeyGesture(Key.S,
                OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control));

        var item = Assert.IsType<NativeMenuItem>(IdeMenu.BuildNative([entry]).Items[0]);

        // On macOS the canonical shortcut is Cmd, elsewhere Ctrl.
        var atteso = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.Equal(atteso, item.Gesture!.KeyModifiers);
    }

    [AvaloniaFact]
    public async Task NoIconStandsForTwoUnrelatedCommands()
    {
        // The rule this replaces demanded an icon on every entry, and what it
        // produced was the Build hammer in front of Format Document. An icon
        // shared by unrelated commands is worse than no icon: it stops being
        // a way to find the entry and becomes noise in front of the words.
        //
        // Pairs that are genuinely the same action wearing two labels — two
        // ways to stop, two ways to go to a symbol — are allowed.
        using var host = new TestWindow();
        await host.SettleAsync();

        var byIcon = new Dictionary<IconKind, List<string>>();

        void Collect(IReadOnlyList<MenuEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.IsSeparator) continue;

                if (entry.Children is { Count: > 0 } children)
                {
                    Collect(children);
                    continue;
                }

                if (entry.Icon == IconKind.None) continue;

                if (!byIcon.TryGetValue(entry.Icon, out var headers))
                    byIcon[entry.Icon] = headers = [];

                if (!headers.Contains(entry.Header)) headers.Add(entry.Header);
            }
        }

        Collect(host.Window.MenuEntriesForTests());

        // Same verb, two entry points: not a collision.
        IconKind[] deliberatelyShared =
        [
            IconKind.Stop, IconKind.GoToSymbol, IconKind.Run,
            IconKind.Properties, IconKind.Find
        ];

        var clashing = byIcon
            .Where(pair => !deliberatelyShared.Contains(pair.Key) && pair.Value.Count > 1)
            .Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}")
            .ToList();

        Assert.True(clashing.Count == 0,
            "These icons stand for unrelated commands:\n" + string.Join("\n", clashing));
    }

    [AvaloniaFact]
    public async Task TheEntriesFoundByShapeKeepTheirIcons()
    {
        // The other direction: dropping icons everywhere would leave a wall
        // of text. The common file and build commands are found by their
        // shape before the eye reads the label.
        using var host = new TestWindow();
        await host.SettleAsync();

        var withIcons = new List<IconKind>();

        void Collect(IReadOnlyList<MenuEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Children is { Count: > 0 } children) Collect(children);
                else if (entry.Icon != IconKind.None) withIcons.Add(entry.Icon);
            }
        }

        Collect(host.Window.MenuEntriesForTests());

        Assert.Contains(IconKind.New, withIcons);
        Assert.Contains(IconKind.Save, withIcons);
        Assert.Contains(IconKind.Build, withIcons);
        Assert.Contains(IconKind.Debug, withIcons);
    }
}
