using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>LinkArea, LinkLabel.Link/LinkCollection and the LinkLabel control: colour, hit-testing and keyboard focus.</summary>
public class LinkLabelTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    // --- LinkArea -----------------------------------------------------

    [Fact]
    public void LinkArea_compares_by_value_and_reports_emptiness()
    {
        var a = new LinkArea(2, 5);
        var b = new LinkArea(2, 5);
        var empty = new LinkArea();

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a.IsEmpty);
        Assert.True(empty.IsEmpty);
        Assert.Equal("{Start=2, Length=5}", a.ToString());
    }

    [Fact]
    public void LinkAreaConverter_round_trips_through_its_text_form()
    {
        var converter = new LinkArea.LinkAreaConverter();
        var invariant = CultureInfo.InvariantCulture;

        var area = (LinkArea)converter.ConvertFrom(null!, invariant, "3, 7")!;
        Assert.Equal(new LinkArea(3, 7), area);

        var text = (string)converter.ConvertTo(null!, invariant, new LinkArea(3, 7), typeof(string))!;
        Assert.Equal("3, 7", text);

        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(null!, invariant, "not-an-area"));
    }

    // --- LinkCollection -------------------------------------------------

    [Fact]
    public void Links_are_kept_sorted_by_Start_however_they_are_added()
    {
        var label = new LinkLabel { Text = "one two three" };

        var two = label.Links.Add(4, 3);
        var one = label.Links.Add(0, 3);
        var three = label.Links.Add(8, 5);

        Assert.Equal(3, label.Links.Count);
        Assert.Same(one, label.Links[0]);
        Assert.Same(two, label.Links[1]);
        Assert.Same(three, label.Links[2]);
        Assert.True(label.Links.LinksAdded);
    }

    [Fact]
    public void Adding_an_overlapping_link_throws()
    {
        var label = new LinkLabel { Text = "one two three" };
        label.Links.Add(0, 7);

        Assert.Throws<InvalidOperationException>(() => label.Links.Add(5, 3));
    }

    [Fact]
    public void Links_are_found_by_name_and_removed_by_key()
    {
        var label = new LinkLabel { Text = "one two three" };
        label.Links.Add(0, 3).Name = "first";
        label.Links.Add(4, 3).Name = "second";

        Assert.NotNull(label.Links["first"]);
        Assert.Equal(1, label.Links.IndexOfKey("second"));

        label.Links.RemoveByKey("first");
        Assert.Single(label.Links);
        Assert.Null(label.Links["first"]);
    }

    // --- Painting --------------------------------------------------------

    private static Bitmap Render(LinkLabel label)
    {
        label.AutoSize = true;
        var bitmap = new Bitmap(Math.Max(label.Width, 1), Math.Max(label.Height, 1));
        label.DrawToBitmap(bitmap, new Rectangle(0, 0, label.Width, label.Height));

        return bitmap;
    }

    private static bool MatchesColor(Color pixel, Color target) =>
        Math.Abs(pixel.R - target.R) < 16 && Math.Abs(pixel.G - target.G) < 16 && Math.Abs(pixel.B - target.B) < 16;

    private static bool BitmapContainsColor(Bitmap bitmap, Color target, Rectangle? within = null)
    {
        var area = within ?? new Rectangle(0, 0, bitmap.Width, bitmap.Height);

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (MatchesColor(bitmap.GetPixel(x, y), target)) return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_links_run_paints_in_LinkColor_and_the_rest_in_ForeColor()
    {
        var label = new LinkLabel { Font = Font, ForeColor = Color.Black, Text = "Hello World", LinkArea = new LinkArea(6, 5), AutoSize = true };
        using var bitmap = Render(label);

        // Find exactly where "World" (the link) starts, through the same
        // hit-testing the mouse uses, rather than an independent measurement
        // that could disagree with OnPaint's by a pixel or two.
        var midY = label.Height / 2;
        var linkStartX = Enumerable.Range(0, label.Width).First(x => InvokePointInLink(label, x, midY) is not null);

        var leftOfLink = new Rectangle(0, 0, Math.Max(linkStartX - 2, 1), bitmap.Height);
        var fromLink = new Rectangle(linkStartX, 0, bitmap.Width - linkStartX, bitmap.Height);

        Assert.True(BitmapContainsColor(bitmap, label.LinkColor, fromLink), "The link ('World') was not painted in LinkColor.");
        Assert.False(BitmapContainsColor(bitmap, label.LinkColor, leftOfLink), "Plain text ('Hello') was painted in LinkColor.");
        Assert.True(BitmapContainsColor(bitmap, Color.Black, leftOfLink), "Plain text ('Hello') was not painted in ForeColor.");
    }

    [Fact]
    public void LinkVisited_paints_the_implicit_link_in_VisitedLinkColor()
    {
        var label = new LinkLabel { Font = Font, Text = "Visit", LinkVisited = true };
        using var bitmap = Render(label);

        Assert.True(BitmapContainsColor(bitmap, label.VisitedLinkColor), "A visited link was not painted in VisitedLinkColor.");
    }

    [Fact]
    public void A_disabled_LinkLabel_paints_its_link_in_DisabledLinkColor()
    {
        var label = new LinkLabel { Font = Font, Text = "Disabled", Enabled = false };
        using var bitmap = Render(label);

        Assert.True(BitmapContainsColor(bitmap, label.DisabledLinkColor), "A disabled link was not painted in DisabledLinkColor.");
    }

    [Fact]
    public void A_disabled_individual_link_paints_in_DisabledLinkColor_even_when_the_control_is_enabled()
    {
        var label = new LinkLabel { Font = Font, Text = "one two" };
        var link = label.Links.Add(0, 3);
        link.Enabled = false;
        using var bitmap = Render(label);

        var linkWidth = TextRenderer.MeasureText("one", Font).Width;
        Assert.True(BitmapContainsColor(bitmap, label.DisabledLinkColor, new Rectangle(0, 0, linkWidth + 2, bitmap.Height)));
    }

    [Fact]
    public void NeverUnderline_draws_no_underline_but_AlwaysUnderline_does()
    {
        // A few extra pixels of height, beyond what AutoSize gives a single
        // line, so the underline below the baseline has room to be visible
        // (an AutoSize'd LinkLabel is exactly one line tall, which can clip it).
        var size = new Size(TextRenderer.MeasureText("Link", Font).Width + 10, Font.Height + 8);
        var never = new LinkLabel { Font = Font, Text = "Link", LinkBehavior = LinkBehavior.NeverUnderline, Size = size };
        var always = new LinkLabel { Font = Font, Text = "Link", LinkBehavior = LinkBehavior.AlwaysUnderline, Size = size };

        using var neverBitmap = new Bitmap(never.Width, never.Height);
        never.DrawToBitmap(neverBitmap, new Rectangle(0, 0, never.Width, never.Height));
        using var alwaysBitmap = new Bitmap(always.Width, always.Height);
        always.DrawToBitmap(alwaysBitmap, new Rectangle(0, 0, always.Width, always.Height));

        // The underline is a thin, anti-aliased stroke blended with the
        // background, not a pure LinkColor pixel, so comparing the two
        // otherwise-identical renderings pixel by pixel is the reliable way
        // to notice it, rather than guessing a colour tolerance.
        var differences = 0;
        for (var y = 0; y < neverBitmap.Height; y++)
        {
            for (var x = 0; x < neverBitmap.Width; x++)
            {
                if (neverBitmap.GetPixel(x, y) != alwaysBitmap.GetPixel(x, y)) differences++;
            }
        }

        Assert.True(differences > 0, "AlwaysUnderline painted exactly the same pixels as NeverUnderline.");
    }

    // --- Hit-testing -------------------------------------------------------

    private static LinkLabel.Link? InvokePointInLink(LinkLabel label, int x, int y) =>
        (LinkLabel.Link?)typeof(LinkLabel).GetMethod("PointInLink", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(label, [x, y]);

    [Fact]
    public void PointInLink_finds_the_link_under_a_point_and_null_elsewhere()
    {
        var label = new LinkLabel { Font = Font, Text = "Link", AutoSize = true };

        var hit = InvokePointInLink(label, label.Width / 2, label.Height / 2);
        var miss = InvokePointInLink(label, label.Width + 50, label.Height + 50);

        Assert.NotNull(hit);
        Assert.Null(miss);
    }

    // --- Mouse ---------------------------------------------------------

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 150) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Hovering_an_enabled_link_shows_the_hand_cursor()
    {
        var label = new LinkLabel { Font = Font, Text = "Link", AutoSize = true, Location = new Point(10, 10) };
        var form = ShowWith(label);

        form.Host!.MouseMove(new Avalonia.Point(10 + label.Width / 2, 10 + label.Height / 2));
        Assert.Equal(Cursors.Hand, label.EffectiveCursor);

        form.Host!.MouseMove(new Avalonia.Point(10 + label.Width + 60, 10 + label.Height + 60));
        Assert.NotEqual(Cursors.Hand, label.EffectiveCursor);

        form.Close();
    }

    [AvaloniaFact]
    public void A_disabled_LinkLabel_shows_no_hand_and_drops_it_when_disabled_under_the_pointer()
    {
        var label = new LinkLabel { Font = Font, Text = "Link", AutoSize = true, Location = new Point(10, 10) };
        var form = ShowWith(label);
        var overTheLink = new Avalonia.Point(10 + label.Width / 2, 10 + label.Height / 2);

        form.Host!.MouseMove(overTheLink);
        Assert.Equal(Cursors.Hand, label.EffectiveCursor);

        label.Enabled = false;
        Assert.NotEqual(Cursors.Hand, label.EffectiveCursor);

        form.Host!.MouseMove(new Avalonia.Point(overTheLink.X + 1, overTheLink.Y));
        Assert.NotEqual(Cursors.Hand, label.EffectiveCursor);

        form.Close();
    }

    [AvaloniaFact]
    public void The_hand_over_a_link_leaves_the_applications_Cursor_untouched()
    {
        var label = new LinkLabel { Font = Font, Text = "Link", AutoSize = true, Location = new Point(10, 10), Cursor = Cursors.Cross };
        var form = ShowWith(label);

        form.Host!.MouseMove(new Avalonia.Point(10 + label.Width / 2, 10 + label.Height / 2));
        Assert.Equal(Cursors.Hand, label.EffectiveCursor);
        Assert.Equal(Cursors.Cross, label.Cursor);

        form.Host!.MouseMove(new Avalonia.Point(10 + label.Width + 60, 10 + label.Height + 60));
        Assert.Equal(Cursors.Cross, label.Cursor);

        form.Close();
    }

    [Fact]
    public void A_link_running_to_the_end_of_the_text_overlaps_any_link_after_its_start()
    {
        var label = new LinkLabel { Text = "Hello wonderful world" };
        label.Links.Clear();
        label.Links.Add(5, -1);

        Assert.Throws<InvalidOperationException>(() => label.Links.Add(10, 3));
    }

    [AvaloniaFact]
    public void Releasing_over_the_pressed_link_raises_LinkClicked_but_releasing_elsewhere_does_not()
    {
        var label = new LinkLabel { Font = Font, ForeColor = Color.Black, Text = "Hello World", LinkArea = new LinkArea(6, 5), AutoSize = true, Location = new Point(0, 0) };
        LinkLabel.Link? clicked = null;
        label.LinkClicked += (_, e) => clicked = e.Link;
        var form = ShowWith(label);

        var helloWidth = TextRenderer.MeasureText("Hello ", Font).Width;
        var worldWidth = TextRenderer.MeasureText("World", Font).Width;
        var insideHello = new Avalonia.Point(helloWidth / 2, label.Height / 2);
        var insideWorld = new Avalonia.Point(helloWidth + worldWidth / 2, label.Height / 2);

        // Pressed on the link, released on plain text: no click.
        form.Host!.MouseDown(insideWorld, MouseButton.Left);
        form.Host!.MouseUp(insideHello, MouseButton.Left);
        Assert.Null(clicked);

        // Pressed and released on the same link: one click, with that link.
        form.Host!.MouseDown(insideWorld, MouseButton.Left);
        form.Host!.MouseUp(insideWorld, MouseButton.Left);
        Assert.NotNull(clicked);
        Assert.Equal(6, clicked!.Start);
        Assert.Equal(5, clicked.Length);

        form.Close();
    }

    [AvaloniaFact]
    public void A_link_shows_ActiveLinkColor_while_the_mouse_holds_it_down()
    {
        var label = new LinkLabel { Font = Font, Text = "Link", AutoSize = true, Location = new Point(0, 0) };
        var form = ShowWith(label);

        form.Host!.MouseDown(new Avalonia.Point(label.Width / 2, label.Height / 2), MouseButton.Left);

        using var pressedBitmap = new Bitmap(label.Width, label.Height);
        label.DrawToBitmap(pressedBitmap, new Rectangle(0, 0, label.Width, label.Height));
        Assert.True(BitmapContainsColor(pressedBitmap, label.ActiveLinkColor), "The link was not painted in ActiveLinkColor while pressed.");

        form.Host!.MouseUp(new Avalonia.Point(label.Width / 2, label.Height / 2), MouseButton.Left);
        form.Close();
    }

    // --- Keyboard --------------------------------------------------------

    [AvaloniaFact]
    public void Tab_moves_between_a_labels_own_links_before_it_leaves_the_control()
    {
        var label = new LinkLabel { Font = Font, Text = "one two", TabIndex = 0 };
        label.Links.Add(0, 3).LinkData = "one";
        label.Links.Add(4, 3).LinkData = "two";
        var next = new Button { TabIndex = 1, Location = new Point(0, 40) };
        var form = ShowWith(label);
        form.Controls.Add(next);
        Dispatcher.UIThread.RunJobs();

        var seen = new List<object?>();
        label.LinkClicked += (_, e) => seen.Add(e.Link?.LinkData);

        label.Focus();
        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(["one", "two"], seen);
        Assert.True(label.Focused, "Tab left the label after only one of its two links.");

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(next.Focused, "Tab did not leave the label once its last link had focus.");

        form.Close();
    }

    [AvaloniaFact]
    public void Space_also_activates_the_focused_link()
    {
        var label = new LinkLabel { Font = Font, Text = "Link" };
        var clicks = 0;
        label.LinkClicked += (_, _) => clicks++;
        var form = ShowWith(label);

        label.Focus();
        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal(1, clicks);

        form.Close();
    }
}
