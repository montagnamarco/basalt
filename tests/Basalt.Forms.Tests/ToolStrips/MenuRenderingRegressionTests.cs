using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

public sealed class MenuRenderingRegressionTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(Math.Max(control.Width, 1), Math.Max(control.Height, 1), PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    /// <summary>A real top-level window behind a control: ShowDropDown's own screen-positioning needs one.</summary>
    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [Fact]
    public void MenuBarItemsUseCompactPaddingAndPreserveExplicitPadding()
    {
        using var menu = new MenuStrip { Size = new Size(300, 24) };
        var file = new ToolStripMenuItem("File");
        menu.Items.Add(file);
        Assert.Equal(8, file.Padding.Horizontal);
        var barWidth = file.GetPreferredSize(Size.Empty).Width;
        using var popup = new ToolStripDropDownMenu();
        popup.Items.Add(file);
        Assert.True(file.GetPreferredSize(Size.Empty).Width > barWidth);
        file.Padding = new Padding(12);
        menu.Items.Add(file);
        Assert.Equal(new Padding(12), file.Padding);
    }

    [Fact]
    public void SelectedProfessionalMenuLabelHasReadableDarkPixels()
    {
        using var menu = new MenuStrip { Size = new Size(220, 28), RenderMode = ToolStripRenderMode.Professional };
        var file = new ToolStripMenuItem("File") { ForeColor = Color.Black };
        menu.Items.Add(file);
        menu.PerformLayout();
        file.SetSelectedInternal(true);
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
        var darkPixels = 0;
        for (var y = file.Bounds.Top + 3; y < Math.Min(file.Bounds.Bottom - 3, bitmap.Height); y++)
            for (var x = file.Bounds.Left + 3; x < Math.Min(file.Bounds.Right - 3, bitmap.Width); x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 200 && color.R < 100 && color.G < 100 && color.B < 100) darkPixels++;
            }
        Assert.True(darkPixels > 5, "The pale selection must retain visible menu label pixels.");
    }

    // --- Authentic Windows drop-down look: a shared shortcut/arrow column, full-width rows, a real
    // border on every edge, and a separator confined to a thin row instead of the whole drop-down. ---

    [Fact]
    public void DropDownWidensForASiblingsLongShortcut()
    {
        using var dropDown = new ToolStripDropDownMenu { Font = Font };
        dropDown.Items.Add(new ToolStripMenuItem("Item") { Font = Font });
        var widthBeforeSibling = dropDown.GetPreferredSize(Size.Empty).Width;

        dropDown.Items.Add(new ToolStripMenuItem("Other") { Font = Font, ShortcutKeys = Keys.Control | Keys.Shift | Keys.End });
        var widthAfterSibling = dropDown.GetPreferredSize(Size.Empty).Width;

        Assert.True(widthAfterSibling > widthBeforeSibling,
            "a sibling's long shortcut must widen the whole drop-down's reserved trailing column, or that sibling's own shortcut text gets clipped once every row shares one width.");
    }

    [Fact]
    public void DropDownRowsStretchToTheFullDisplayWidth()
    {
        using var dropDown = new ToolStripDropDownMenu { Font = Font, AutoSize = false, Size = new Size(220, 80) };
        var narrow = new ToolStripMenuItem("A") { Font = Font };
        dropDown.Items.Add(narrow);
        dropDown.Items.Add(new ToolStripMenuItem("A rather longer caption") { Font = Font });
        dropDown.PerformLayout();

        Assert.Equal(dropDown.DisplayRectangle.Width, narrow.Bounds.Width);
        Assert.Equal(dropDown.DisplayRectangle.Left, narrow.Bounds.Left);
    }

    [Fact]
    public void ADropDownIsAVerticalStripAndItsSeparatorStaysAThinRow()
    {
        using var dropDown = new ToolStripDropDownMenu { Font = Font };
        dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        var separator = new ToolStripSeparator();
        dropDown.Items.Add(separator);
        dropDown.Items.Add(new ToolStripMenuItem("Two") { Font = Font });
        dropDown.PerformLayout();

        Assert.True(dropDown.StacksVertically);
        Assert.True(separator.Bounds.Height < dropDown.Height,
            "a menu separator is one thin row, not a bar spanning the whole drop-down's height.");
    }

    [Fact]
    public void SeparatorLineStartsAfterTheImageMarginNotAcrossIt()
    {
        // The System renderer's image margin is a flat fill, unlike Professional's vertical gradient:
        // a flat colour lets this test tell "no line was drawn here" apart from "a different, nearby
        // row's gradient shade happens to differ", which a gradient would make the same comparison moot.
        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(160, 90), RenderMode = ToolStripRenderMode.System,
        };
        dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        var separator = new ToolStripSeparator();
        dropDown.Items.Add(separator);
        dropDown.Items.Add(new ToolStripMenuItem("Two") { Font = Font });
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);
        var marginX = dropDown.ClientRectangle.Left + 4;
        var pastMarginX = dropDown.DisplayRectangle.Left + 4;
        var plainRowY = separator.Bounds.Bottom + 4;

        // Nothing is drawn over the image margin column on the separator's own rows: that column looks
        // the same there as it does on an ordinary row, for every row the etched line itself spans.
        var marginUnaffected = true;
        var lineDrawnPastMargin = false;
        for (var y = separator.Bounds.Top; y < separator.Bounds.Bottom; y++)
        {
            if (bitmap.GetPixel(marginX, y).ToArgb() != bitmap.GetPixel(marginX, plainRowY).ToArgb()) marginUnaffected = false;
            if (bitmap.GetPixel(pastMarginX, y).ToArgb() != bitmap.GetPixel(pastMarginX, plainRowY).ToArgb()) lineDrawnPastMargin = true;
        }

        Assert.True(marginUnaffected, "the etched separator line must not reach into the image margin column.");
        Assert.True(lineDrawnPastMargin, "the etched separator line must still be drawn past the image margin column.");
    }

    [Fact]
    public void DropDownPaintsAFullRectangleBorderNotJustABottomLine()
    {
        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(120, 60), RenderMode = ToolStripRenderMode.Professional,
        };
        dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);
        var interior = bitmap.GetPixel(dropDown.Width / 2, dropDown.Height / 2).ToArgb();

        // A drop-down floats free of any other surface, so (unlike a docked toolbar, which only meets
        // the form along one edge) it needs a visible border on every edge, not just the bottom one.
        Assert.NotEqual(interior, bitmap.GetPixel(2, 0).ToArgb());
        Assert.NotEqual(interior, bitmap.GetPixel(0, 2).ToArgb());
    }

    // --- Windows 10/11 look: a flat hover fill (not a raised bevel), a near-white drop-down body (not
    // the toolbar's grey gradient), and a mnemonic underline that waits for the keyboard. ---

    [Fact]
    public void HoveredMenuItemIsAFlatPaleFillWithADarkerOnePixelBorderNotARaisedBevel()
    {
        var colors = new ToolStripProfessionalRenderer().ColorTable;
        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(140, 60), RenderMode = ToolStripRenderMode.Professional,
        };
        var item = new ToolStripMenuItem("Item") { Font = Font };
        dropDown.Items.Add(item);
        dropDown.PerformLayout();
        item.SetSelectedInternal(true);

        using var bitmap = Draw(dropDown);
        var bounds = item.Bounds;
        var midX = bounds.Left + bounds.Width / 2;
        var nearTop = bitmap.GetPixel(midX, bounds.Top + 3).ToArgb();
        var nearBottom = bitmap.GetPixel(midX, bounds.Bottom - 4).ToArgb();
        var topBorder = bitmap.GetPixel(midX, bounds.Top).ToArgb();

        // Flat: the fill a few pixels below the top border is the same colour as the fill a few pixels
        // above the bottom one - a raised bevel (light top edge, dark bottom edge, shaded face) or a
        // multi-stop gradient would not agree like this, and both equal ColorTable.MenuItemSelected
        // exactly, not some blend of it.
        Assert.Equal(colors.MenuItemSelected.ToArgb(), nearTop);
        Assert.Equal(colors.MenuItemSelected.ToArgb(), nearBottom);

        // The border is a distinct, slightly darker shade of the same fill, not the bevel's own
        // light/dark pair (SystemColors.ControlLightLight / ControlDark the System renderer paints).
        Assert.Equal(colors.MenuItemBorder.ToArgb(), topBorder);
        Assert.NotEqual(colors.MenuItemSelected.ToArgb(), colors.MenuItemBorder.ToArgb());
    }

    [Fact]
    public void DropDownBodyIsAFlatNearWhiteFillNotTheToolbarsGreyGradient()
    {
        var colors = new ToolStripProfessionalRenderer().ColorTable;
        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(140, 70), RenderMode = ToolStripRenderMode.Professional,
        };
        dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);
        var x = dropDown.DisplayRectangle.Right - 4;
        var nearTop = bitmap.GetPixel(x, 2).ToArgb();
        var nearBottom = bitmap.GetPixel(x, dropDown.Height - 3).ToArgb();

        // Flat: top and bottom of the body agree, unlike the toolbar's own vertical gradient.
        Assert.Equal(nearTop, nearBottom);
        Assert.Equal(colors.ToolStripDropDownBackground.ToArgb(), nearTop);

        // Not the plain bar's own fill: a MenuStrip (never a ToolStripDropDown) still gets the toolbar
        // gradient's own first colour, which this drop-down's body must not show instead.
        using var bar = new MenuStrip { Font = Font, AutoSize = false, Size = new Size(140, 24), RenderMode = ToolStripRenderMode.Professional };
        bar.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        bar.PerformLayout();
        using var barBitmap = Draw(bar);
        Assert.NotEqual(barBitmap.GetPixel(2, 2).ToArgb(), nearTop);
    }

    [Fact]
    public void MnemonicUnderlineHiddenUntilKeyboardCuesAreVisible()
    {
        var original = ToolStripDropDown.KeyboardCuesVisible;
        try
        {
            // A large font makes the underline stroke's few pixels easy to tell apart from anti-aliasing
            // noise around the glyphs themselves.
            using var bigFont = new Font("Microsoft Sans Serif", 24f);
            using var dropDown = new ToolStripDropDownMenu
            {
                Font = bigFont, AutoSize = false, Size = new Size(260, 60), RenderMode = ToolStripRenderMode.Professional,
            };
            var item = new ToolStripMenuItem("&Edit") { Font = bigFont, ForeColor = Color.Black };
            dropDown.Items.Add(item);
            dropDown.PerformLayout();

            ToolStripDropDown.KeyboardCuesVisible = false;
            using var withoutCues = Draw(dropDown);

            // The strip and item are otherwise untouched: only the static flag (and the invalidation a
            // real Invalidate()/Click would also trigger) changes between the two renders.
            ToolStripDropDown.KeyboardCuesVisible = true;
            dropDown.Invalidate();
            item.Invalidate();
            using var withCues = Draw(dropDown);

            var differingPixels = 0;
            for (var y = item.Bounds.Top; y < item.Bounds.Bottom; y++)
                for (var x = item.Bounds.Left; x < item.Bounds.Right; x++)
                {
                    if (withoutCues.GetPixel(x, y).ToArgb() != withCues.GetPixel(x, y).ToArgb()) differingPixels++;
                }

            // Hiding the prefix removes only the underline stroke: the letters themselves are pixel for
            // pixel identical either way, so the only place the two renders can differ is wherever the
            // mnemonic's underline is drawn once keyboard cues are visible.
            Assert.True(differingPixels > 0,
                "a mnemonic's underline must stay hidden until keyboard cues are visible, and appear once they are.");
        }
        finally
        {
            ToolStripDropDown.KeyboardCuesVisible = original;
        }
    }

    [AvaloniaFact]
    public void KeyboardNavigationAndMnemonicsMakeCuesVisibleButAFreshMouseClickHidesThemAgain()
    {
        var original = ToolStripDropDown.KeyboardCuesVisible;
        try
        {
            using var dropDown = new ToolStripDropDownMenu { Font = Font };
            dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
            dropDown.PerformLayout();

            ToolStripDropDown.KeyboardCuesVisible = false;
            dropDown.ProcessNavigationKey(Keys.Down);
            Assert.True(ToolStripDropDown.KeyboardCuesVisible, "an arrow key reaching an open drop-down must reveal keyboard cues.");

            var bar = new MenuStrip { Font = Font };
            var file = new ToolStripMenuItem("&File") { Font = Font };
            file.DropDownItems.Add(new ToolStripMenuItem("Open") { Font = Font });
            bar.Items.Add(file);
            var form = ShowWith(bar);

            ToolStripDropDown.KeyboardCuesVisible = false;
            file.ProcessMnemonic('F');
            Assert.True(ToolStripDropDown.KeyboardCuesVisible, "a mnemonic reaching an item must reveal keyboard cues.");
            file.HideDropDown();

            ToolStripDropDown.KeyboardCuesVisible = true;
            InvokeOnMouseDown(file, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
            Assert.False(ToolStripDropDown.KeyboardCuesVisible,
                "a plain mouse click opening a menu bar's own top item, with nothing already open, must hide keyboard cues again.");

            form.Close();
        }
        finally
        {
            ToolStripDropDown.KeyboardCuesVisible = original;
        }
    }

    [AvaloniaFact]
    public void ARightClickContextMenuHidesKeyboardCuesLeftOverFromAnEarlierKeyboardMenu()
    {
        var original = ToolStripDropDown.KeyboardCuesVisible;
        try
        {
            using var menu = new ContextMenuStrip { Font = Font };
            menu.Items.Add(new ToolStripMenuItem("&Copy") { Font = Font });
            var target = new Panel { Size = new Size(100, 100), ContextMenuStrip = menu };
            var form = new Form { ClientSize = new Size(200, 200) };
            form.Controls.Add(target);
            form.Show();

            ToolStripDropDown.KeyboardCuesVisible = true;
            target.RaiseMouseUp(MouseButtons.Right, 1, new Point(10, 10), true);

            Assert.True(menu.Visible, "the right-click did not open the context menu.");
            Assert.False(ToolStripDropDown.KeyboardCuesVisible,
                "a context menu opened with the mouse must not underline mnemonics left visible by an earlier keyboard menu.");

            menu.Close();
            form.Close();
        }
        finally
        {
            ToolStripDropDown.KeyboardCuesVisible = original;
        }
    }

    private static void InvokeOnMouseDown(ToolStripItem item, MouseEventArgs e) =>
        typeof(ToolStripItem).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(item, [e]);

    // --- Windows 10 Professional look, round two: the widest caption in a shared shortcut column must
    // still fit, the submenu arrow is a small dark-grey glyph (not a big black triangle), and the image
    // margin is a light grey next to the drop-down's near-white body (not SystemColors' beige fallback). ---

    [Fact]
    public void DropDownWidensEnoughForItsWidestCaptionNextToASharedShortcutColumn()
    {
        // Reproduces the context-menu screenshot regression: "Delete", this group's widest caption, was
        // sized as if the row's own text column ran right up to where the shared "Ctrl+X"/"Ctrl+C"/
        // "Ctrl+V" shortcut column starts, with no room for the few pixels of gap OnPaint's own textLeft
        // still adds after the image margin - clipping the caption's own last glyph by exactly that gap.
        // A size check, not a pixel one: the row's own available text width must still fit the caption
        // it measured itself against, in whole pixels, independent of a particular font's antialiasing.
        using var menu = new ContextMenuStrip { Font = Font };
        var cut = new ToolStripMenuItem("Cut") { Font = Font, ShortcutKeys = Keys.Control | Keys.X };
        var copy = new ToolStripMenuItem("Copy") { Font = Font, ShortcutKeys = Keys.Control | Keys.C };
        var paste = new ToolStripMenuItem("Paste") { Font = Font, ShortcutKeys = Keys.Control | Keys.V };
        var delete = new ToolStripMenuItem("Delete") { Font = Font };
        menu.Items.AddRange([cut, copy, paste, delete]);
        menu.PerformLayout();

        var (shortcutColumnWidth, anySubmenu) = menu.MeasureTrailingColumns();
        const int shortcutColumnGap = 10; // ToolStripMenuItem's own ShortcutColumnGap.
        const int textLeadingGap = 4; // ToolStripMenuItem's own TextLeadingGap.
        // ToolStripMenuItem's own leading image/check column, drawn inside each row's own bounds (which
        // already start past the drop-down's ImageMarginWidth - see DisplayRectangle): a second, smaller
        // margin of its own, not the same number as the drop-down's.
        const int rowOwnImageMargin = 22;
        const int shortcutTrailingGap = 8; // ToolStripMenuItem's own ShortcutTrailingGap, used when no arrow column is reserved.
        var trailingReserve = (anySubmenu ? delete.Bounds.Height : 0)
            + (shortcutColumnWidth > 0 ? shortcutColumnWidth + shortcutColumnGap : 0)
            + (!anySubmenu && shortcutColumnWidth > 0 ? shortcutTrailingGap : 0);
        var textWidth = TextRenderer.MeasureText("Delete", Font).Width;

        var availableForText = delete.Bounds.Width - rowOwnImageMargin - textLeadingGap - trailingReserve;

        Assert.True(availableForText >= textWidth,
            $"the row is only {availableForText}px wide for \"Delete\"'s own {textWidth}px of text once the image margin, the leading gap and the shared shortcut column are reserved.");
    }

    [Fact]
    public void SubmenuArrowIsASmallDarkGreyGlyphNotABigBlackTriangle()
    {
        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(160, 40), RenderMode = ToolStripRenderMode.Professional, BackColor = Color.White,
        };
        var item = new ToolStripMenuItem("More") { Font = Font, ForeColor = Color.Black };
        item.DropDownItems.Add(new ToolStripMenuItem("Sub"));
        dropDown.Items.Add(item);
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);

        var arrowPixels = 0;
        var blackPixels = 0;
        for (var y = item.Bounds.Top; y < item.Bounds.Bottom; y++)
            for (var x = item.Bounds.Left; x < item.Bounds.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.ToArgb() == Color.White.ToArgb()) continue;
                if (color.ToArgb() == Color.Black.ToArgb())
                {
                    // "More"'s own caption text also paints black pixels in this row; only count ones
                    // past where the caption's text column ends, close to the trailing edge, as the arrow.
                    if (x > item.Bounds.Right - 16) blackPixels++;
                    continue;
                }
                if (x > item.Bounds.Right - 16) arrowPixels++;
            }

        // A real Windows 10 glyph is roughly 4x7 px at 100% scaling: comfortably under three dozen
        // pixels, where the old symmetric triangle (confined to a square around the whole reserved arrow
        // column) painted well over a hundred.
        Assert.InRange(arrowPixels, 1, 40);
        Assert.Equal(0, blackPixels);
    }

    [Fact]
    public void ImageMarginIsLightGreyNotSystemColorsBeigeFallback()
    {
        var colors = new ToolStripProfessionalRenderer().ColorTable;

        // SystemColors.Control/.ControlLight (the old source for these) are .NET's fixed, non-Windows
        // fallback values: a beige, Windows-2000-era "Control" face, wherever this test runs without a
        // live desktop theme to query - the same beige the screenshots showed instead of Windows 10's
        // light grey.
        var beige = SystemColors.Control;
        Assert.NotEqual(beige.ToArgb(), colors.ImageMarginGradientBegin.ToArgb());
        Assert.NotEqual(beige.ToArgb(), colors.ImageMarginGradientEnd.ToArgb());

        // Light grey: red, green and blue agree closely (beige's red/green run well ahead of its blue),
        // and every stop sits in Windows 10's own light-grey family, clearly short of white.
        foreach (var color in new[] { colors.ImageMarginGradientBegin, colors.ImageMarginGradientMiddle, colors.ImageMarginGradientEnd })
        {
            Assert.InRange(color.R, 0xE0, 0xFA);
            Assert.True(Math.Abs(color.R - color.G) <= 4 && Math.Abs(color.G - color.B) <= 4,
                $"{color} is not a neutral grey.");
        }

        using var dropDown = new ToolStripDropDownMenu
        {
            Font = Font, AutoSize = false, Size = new Size(140, 40), RenderMode = ToolStripRenderMode.Professional,
        };
        dropDown.Items.Add(new ToolStripMenuItem("One") { Font = Font });
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);
        var marginPixel = bitmap.GetPixel(dropDown.ClientRectangle.Left + 4, dropDown.Height / 2);

        Assert.NotEqual(beige.ToArgb(), marginPixel.ToArgb());
        Assert.InRange(marginPixel.R, 0xE0, 0xFA);
        Assert.True(Math.Abs(marginPixel.R - marginPixel.G) <= 4 && Math.Abs(marginPixel.G - marginPixel.B) <= 4,
            $"{marginPixel} is not a neutral grey.");
    }

    /// <summary>The usual dark-pixel count used elsewhere in this file, generalised to any rectangle.</summary>
    [Fact]
    public void AShortcutLeavesAirBeforeTheDropDownsRightBorder()
    {
        // The context-menu screenshot had "Ctrl+X" touching the border; Windows keeps a gap.
        using var menu = new ContextMenuStrip { Font = Font, RenderMode = ToolStripRenderMode.Professional, BackColor = Color.White };
        var cut = new ToolStripMenuItem("Cut") { Font = Font, ForeColor = Color.Black, ShortcutKeys = Keys.Control | Keys.X };
        menu.Items.Add(cut);
        menu.PerformLayout();

        using var bitmap = Draw(menu);
        var bounds = cut.Bounds;
        var rightmostInk = -1;
        for (var x = bounds.Left; x < Math.Min(bounds.Right, bitmap.Width); x++)
            if (CountDarkPixels(bitmap, new Rectangle(x, bounds.Top + 2, 1, bounds.Height - 4)) > 0) rightmostInk = x;

        Assert.True(rightmostInk > 0, "the shortcut text was not drawn");
        Assert.True(menu.Width - 1 - rightmostInk >= 8,
            $"the shortcut's last ink is at x={rightmostInk}, {menu.Width - 1 - rightmostInk}px from the right border of a {menu.Width}px drop-down.");
    }

    [Fact]
    public void ATallItemsArrowColumnIsMeasuredAtItsOwnHeight()
    {
        // OnPaint reserves the arrow column at the row's real height; measuring it at the 22px default
        // made a big-font menu too narrow for the caption it then painted.
        using var menu = new ContextMenuStrip { Font = Font };
        var big = new ToolStripMenuItem("Tall") { Font = Font, AutoSize = false, Size = new Size(120, 40) };
        var parent = new ToolStripMenuItem("More") { Font = Font };
        parent.DropDownItems.Add(new ToolStripMenuItem("Child"));
        menu.Items.AddRange([big, parent]);
        menu.PerformLayout();

        Assert.Equal(40 - big.Padding.Right, big.ExtraWidthForSharedTrailingColumns());
    }

    private static int CountDarkPixels(Bitmap bitmap, Rectangle bounds)
    {
        var count = 0;
        for (var y = bounds.Top; y < Math.Min(bounds.Bottom, bitmap.Height); y++)
            for (var x = bounds.Left; x < Math.Min(bounds.Right, bitmap.Width); x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 200 && color.R < 100 && color.G < 100 && color.B < 100) count++;
            }
        return count;
    }
}
