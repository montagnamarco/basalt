using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStripManager.Merge/RevertMerge (moving a strip's items into another
/// and back) and SaveSettings/LoadSettings (a form's toolstrips' Visible and
/// Location, round-tripped through a file under Application.UserAppDataPath).
/// </summary>
public class ToolStripManagerMergeAndSettingsTests
{
    [Fact]
    public void Merge_moves_every_item_to_the_target_in_order()
    {
        var source = new ToolStrip();
        var target = new ToolStrip();
        var one = new ToolStripButton("One");
        var two = new ToolStripButton("Two");
        source.Items.AddRange([one, two]);

        var merged = ToolStripManager.Merge(source, target);

        Assert.True(merged);
        Assert.Empty(source.Items);
        Assert.Equal([one, two], target.Items.Cast<ToolStripItem>());
        Assert.Same(target, one.Owner);
    }

    [Fact]
    public void RevertMerge_puts_every_item_back_where_it_was()
    {
        var source = new ToolStrip();
        var target = new ToolStrip();
        var one = new ToolStripButton("One");
        var two = new ToolStripButton("Two");
        source.Items.AddRange([one, two]);
        ToolStripManager.Merge(source, target);

        var reverted = ToolStripManager.RevertMerge(target);

        Assert.True(reverted);
        Assert.Empty(target.Items);
        Assert.Equal([one, two], source.Items.Cast<ToolStripItem>());
    }

    [Fact]
    public void RevertMerge_without_a_prior_Merge_does_nothing()
    {
        Assert.False(ToolStripManager.RevertMerge(new ToolStrip()));
    }

    [Fact]
    public void Reverting_a_chain_of_merges_out_of_order_leaves_the_item_owned_by_exactly_one_strip()
    {
        var stripA = new ToolStrip();
        var stripB = new ToolStrip();
        var stripC = new ToolStrip();
        var item = new ToolStripButton("One");
        stripA.Items.Add(item);

        ToolStripManager.Merge(stripA, stripB);
        ToolStripManager.Merge(stripB, stripC);

        // Reverted in the wrong order: B's merge first, while the item is actually still in C.
        var revertedB = ToolStripManager.RevertMerge(stripB);
        var revertedC = ToolStripManager.RevertMerge(stripC);

        Assert.True(revertedB);
        Assert.True(revertedC);

        // Whichever strip ends up with it, it is owned by exactly one: no stale copy left in two collections.
        var owners = new[] { stripA, stripB, stripC }.Count(strip => strip.Items.Contains(item));
        Assert.Equal(1, owners);
        Assert.Same(item.Owner, new[] { stripA, stripB, stripC }.Single(strip => strip.Items.Contains(item)));

        // Reverting again (nothing left to revert) must not throw or resurrect a stale record.
        Assert.False(ToolStripManager.RevertMerge(stripB));
        Assert.False(ToolStripManager.RevertMerge(stripC));
    }

    [Fact]
    public void Merge_by_name_finds_the_tracked_target_strip()
    {
        var source = new ToolStrip();
        var item = new ToolStripButton("One");
        source.Items.Add(item);
        var target = new ToolStrip { Name = $"NamedTarget-{Guid.NewGuid()}" };

        var merged = ToolStripManager.Merge(source, target.Name);

        Assert.True(merged);
        Assert.Same(target, item.Owner);
        Assert.Same(target, ToolStripManager.FindToolStrip(target.Name));
    }

    [AvaloniaFact]
    public void SaveSettings_then_LoadSettings_restores_Visible_and_Location()
    {
        var key = $"ToolStripManagerTests-{Guid.NewGuid()}";
        var form = new Form();
        var strip = new ToolStrip { Name = "Main", Dock = DockStyle.None, Visible = true, Location = new Point(5, 5) };
        form.Controls.Add(strip);
        form.Show();

        // Settings must land in a throwaway folder, never the real user's Application.UserAppDataPath.
        var tempRoot = Path.Combine(Path.GetTempPath(), "BasaltToolStripSettingsTests-" + Guid.NewGuid());
        ToolStripManager.SettingsRootOverride = tempRoot;

        try
        {
            ToolStripManager.SaveSettings(form, key);

            strip.Visible = false;
            strip.Location = new Point(99, 99);

            ToolStripManager.LoadSettings(form, key);

            Assert.True(strip.Visible);
            Assert.Equal(new Point(5, 5), strip.Location);
        }
        finally
        {
            ToolStripManager.SettingsRootOverride = null;
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public void LoadSettings_does_not_throw_when_two_strips_share_a_Name()
    {
        var key = $"ToolStripManagerTests-{Guid.NewGuid()}";
        var form = new Form();
        var first = new ToolStrip { Name = "Dup", Dock = DockStyle.None, Visible = true, Location = new Point(1, 1) };
        var second = new ToolStrip { Name = "Dup", Dock = DockStyle.None, Visible = true, Location = new Point(2, 2) };
        form.Controls.Add(first);
        form.Controls.Add(second);
        form.Show();

        var tempRoot = Path.Combine(Path.GetTempPath(), "BasaltToolStripSettingsTests-" + Guid.NewGuid());
        ToolStripManager.SettingsRootOverride = tempRoot;

        try
        {
            ToolStripManager.SaveSettings(form, key);

            var exception = Record.Exception(() => ToolStripManager.LoadSettings(form, key));

            Assert.Null(exception);
        }
        finally
        {
            ToolStripManager.SettingsRootOverride = null;
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void IsValidShortcut_requires_a_modifier_or_a_non_typing_key()
    {
        Assert.False(ToolStripManager.IsValidShortcut(Keys.A));
        Assert.True(ToolStripManager.IsValidShortcut(Keys.Control | Keys.A));
        Assert.True(ToolStripManager.IsValidShortcut(Keys.F5));
        Assert.False(ToolStripManager.IsValidShortcut(Keys.None));
    }

    [Fact]
    public void IsShortcutDefined_finds_a_menu_items_shortcut()
    {
        var strip = new ToolStrip();
        var item = new ToolStripMenuItem("Save") { ShortcutKeys = Keys.Control | Keys.S };
        strip.Items.Add(item);

        Assert.True(ToolStripManager.IsShortcutDefined(Keys.Control | Keys.S));
        Assert.False(ToolStripManager.IsShortcutDefined(Keys.Control | Keys.Shift | Keys.Z));
    }
}
