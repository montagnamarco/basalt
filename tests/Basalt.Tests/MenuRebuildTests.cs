using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// What happens to the menu when it is built a second time.
/// </summary>
/// <remarks>
/// Every file opened rebuilds the Recent list and the menu with it, so this
/// happens dozens of times in a session.
/// </remarks>
public class MenuRebuildTests
{
    [AvaloniaFact]
    public void RebuildingKeepsTheSameMenuObject()
    {
        // On macOS the menu bar belongs to the operating system, and Avalonia
        // hands it the NativeMenu instance it was given. Installing a
        // different instance while that one is being pushed across terminates
        // the application — "The menu being updated does not match" — which
        // is what happened when a file was opened from the Problems panel:
        // opening it rebuilt the menu from inside a menu update.
        //
        // Refilling the instance already installed means the object the
        // platform holds never changes, so there is nothing to disagree
        // about. Off macOS this asserts nothing about the platform and
        // everything about not throwing the menu away for no reason.
        using var host = new TestWindow();

        if (!IdeMenu.UsesSystemMenuBar) return;

        var before = NativeMenu.GetMenu(host.Window);

        Assert.NotNull(before);

        host.Window.RebuildMenuForTests();

        Assert.Same(before, NativeMenu.GetMenu(host.Window));
    }

    [AvaloniaFact]
    public void RebuildingStillFillsTheMenu()
    {
        // Reusing the instance must not leave it empty: refilling it means
        // clearing it first, and a menu bar that empties on the first file
        // opened is worse than one that crashes, because nobody reports it.
        using var host = new TestWindow();

        if (!IdeMenu.UsesSystemMenuBar) return;

        var menu = NativeMenu.GetMenu(host.Window)!;
        var before = menu.Items.Count;

        Assert.True(before > 0, "the menu was empty to begin with");

        host.Window.RebuildMenuForTests();

        Assert.Equal(before, NativeMenu.GetMenu(host.Window)!.Items.Count);
    }

    [AvaloniaFact]
    public void RebuildingTwiceIsStillFine()
    {
        // Opening several files in a row, which is the ordinary case.
        using var host = new TestWindow();

        if (!IdeMenu.UsesSystemMenuBar) return;

        var menu = NativeMenu.GetMenu(host.Window)!;
        var expected = menu.Items.Count;

        host.Window.RebuildMenuForTests();
        host.Window.RebuildMenuForTests();
        host.Window.RebuildMenuForTests();

        Assert.Same(menu, NativeMenu.GetMenu(host.Window));
        Assert.Equal(expected, menu.Items.Count);
    }
}
