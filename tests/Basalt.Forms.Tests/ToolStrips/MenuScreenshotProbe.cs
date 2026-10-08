using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// Renders a MenuStrip bar, a fully populated File drop-down and a
/// ContextMenuStrip to PNG files for a human to look at next to a real
/// Windows screenshot.
///
/// Every fact here is skipped unless <c>BASALT_SCREENSHOT_DIR</c> is set:
/// these are pictures, not assertions about behaviour, so nothing in the
/// ordinary test run should spend time producing files nobody asked for.
/// Point the variable at a folder and rerun this file alone
/// (<c>dotnet test tests/Basalt.Forms.Tests --filter "FullyQualifiedName~MenuScreenshotProbe"</c>)
/// to refresh the set.
/// </summary>
[Trait("Category", "Screenshots")]
public sealed class MenuScreenshotProbe
{
    private static readonly Font Font = SystemFonts.MenuFont;

    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable("BASALT_SCREENSHOT_DIR");

    private static void SkipUnlessRequested() =>
        Assert.SkipWhen(string.IsNullOrEmpty(OutputDirectory),
            "Set BASALT_SCREENSHOT_DIR to render screenshots.");

    private static void Save(Control control, string name)
    {
        control.PerformLayout();

        using var bitmap = new Bitmap(Math.Max(control.Width, 1), Math.Max(control.Height, 1), PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));

        var path = Path.Combine(OutputDirectory!, name);
        bitmap.Save(path, ImageFormat.Png);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 200, $"{name} is suspiciously small.");
    }

    /// <summary>A 16x16 placeholder glyph, the same way a toolbox icon would be supplied.</summary>
    private static Image MakeImage()
    {
        var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(255, 60, 120, 215));
        graphics.FillEllipse(brush, 2, 2, 12, 12);

        return bitmap;
    }

    /// <summary>The File drop-down the task describes: New, Open…, a separator, a disabled Save, a checked item, a submenu, Exit.</summary>
    private static (ToolStripMenuItem File, ToolStripDropDownMenu DropDown, ToolStripMenuItem Open) BuildFileMenu()
    {
        var file = new ToolStripMenuItem("&File") { Font = Font };

        var newItem = new ToolStripMenuItem("&New", MakeImage(), (EventHandler?)null) { Font = Font, ShortcutKeys = Keys.Control | Keys.N };
        var open = new ToolStripMenuItem("&Open…") { Font = Font, ShortcutKeys = Keys.Control | Keys.O };
        var save = new ToolStripMenuItem("&Save") { Font = Font, Enabled = false };
        var wordWrap = new ToolStripMenuItem("&Word Wrap") { Font = Font, Checked = true, CheckOnClick = true };
        var recent = new ToolStripMenuItem("&Recent Files") { Font = Font };
        recent.DropDownItems.Add(new ToolStripMenuItem("report.vb") { Font = Font });
        var exit = new ToolStripMenuItem("E&xit") { Font = Font, ShortcutKeys = Keys.Alt | Keys.F4, ShortcutKeyDisplayString = "Alt+F4" };

        file.DropDownItems.Add(newItem);
        file.DropDownItems.Add(open);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(save);
        file.DropDownItems.Add(wordWrap);
        file.DropDownItems.Add(recent);
        file.DropDownItems.Add(exit);

        var dropDown = (ToolStripDropDownMenu)file.DropDown;
        dropDown.Font = Font;

        return (file, dropDown, open);
    }

    [Fact]
    public void MenuBar()
    {
        SkipUnlessRequested();

        var (file, _, _) = BuildFileMenu();
        var edit = new ToolStripMenuItem("&Edit") { Font = Font };
        var view = new ToolStripMenuItem("&View") { Font = Font };
        var help = new ToolStripMenuItem("&Help") { Font = Font };

        using var menu = new MenuStrip { Font = Font, Size = new Size(400, 24) };
        menu.Items.AddRange([file, edit, view, help]);

        Save(menu, "menu-bar.png");
    }

    [Fact]
    public void FileDropDown()
    {
        SkipUnlessRequested();

        var (_, dropDown, open) = BuildFileMenu();
        dropDown.PerformLayout();
        open.SetSelectedInternal(true);

        Save(dropDown, "file-dropdown.png");
    }

    [Fact]
    public void ContextMenu()
    {
        SkipUnlessRequested();

        using var menu = new ContextMenuStrip { Font = Font };
        var cut = new ToolStripMenuItem("Cu&t") { Font = Font, ShortcutKeys = Keys.Control | Keys.X };
        var copy = new ToolStripMenuItem("&Copy") { Font = Font, ShortcutKeys = Keys.Control | Keys.C };
        var paste = new ToolStripMenuItem("&Paste") { Font = Font, ShortcutKeys = Keys.Control | Keys.V };
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new ToolStripSeparator());
        var delete = new ToolStripMenuItem("&Delete") { Font = Font, Enabled = false };
        menu.Items.Add(delete);
        menu.PerformLayout();
        copy.SetSelectedInternal(true);

        Save(menu, "context-menu.png");
    }
}
