using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ScrollBar, ProgressBar, TrackBar and PictureBox: ranges, classic drawing, and headless input.</summary>
public class RangeControlsTests
{
    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static Bitmap SolidImage(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, color);
        }

        return bitmap;
    }

    // --- ScrollBar: Minimum/Maximum/Value clamping ---

    [Fact]
    public void A_scroll_bars_Value_outside_Minimum_and_Maximum_throws()
    {
        var scrollBar = new HScrollBar { Minimum = 0, Maximum = 100 };

        Assert.Throws<ArgumentOutOfRangeException>(() => scrollBar.Value = 101);
        Assert.Throws<ArgumentOutOfRangeException>(() => scrollBar.Value = -1);
    }

    [Fact]
    public void Raising_a_scroll_bars_Minimum_above_Value_clamps_it_and_lowering_Maximum_too()
    {
        var scrollBar = new HScrollBar { Minimum = 0, Maximum = 100, Value = 50 };

        scrollBar.Minimum = 60;
        Assert.Equal(60, scrollBar.Value);

        scrollBar.Maximum = 55;
        Assert.Equal(55, scrollBar.Value);
        Assert.Equal(55, scrollBar.Minimum);
    }

    // --- ScrollBar: event sequences driven by headless mouse and keyboard ---

    [AvaloniaFact]
    public void Clicking_the_down_arrow_raises_SmallIncrement_then_EndScroll()
    {
        var scrollBar = new HScrollBar { Bounds = new Rectangle(0, 0, 100, 16), Minimum = 0, Maximum = 100, Value = 10 };
        var form = ShowWith(scrollBar);
        var events = new List<ScrollEventType>();
        scrollBar.Scroll += (_, e) => events.Add(e.Type);

        form.Host!.MouseDown(new Avalonia.Point(92, 8), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(92, 8), MouseButton.Left);

        Assert.Equal([ScrollEventType.SmallIncrement, ScrollEventType.EndScroll], events);
        Assert.Equal(11, scrollBar.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_track_past_the_thumb_raises_LargeIncrement_then_EndScroll()
    {
        var scrollBar = new HScrollBar { Bounds = new Rectangle(0, 0, 100, 16), Minimum = 0, Maximum = 100, Value = 0, LargeChange = 10 };
        var form = ShowWith(scrollBar);
        var events = new List<ScrollEventType>();
        scrollBar.Scroll += (_, e) => events.Add(e.Type);

        // The thumb sits near the left end of the track at Value 0; the middle of the track is past it.
        form.Host!.MouseDown(new Avalonia.Point(70, 8), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(70, 8), MouseButton.Left);

        Assert.Equal([ScrollEventType.LargeIncrement, ScrollEventType.EndScroll], events);
        Assert.Equal(10, scrollBar.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_the_thumb_raises_ThumbTrack_while_moving_then_ThumbPosition_and_EndScroll()
    {
        var scrollBar = new HScrollBar { Bounds = new Rectangle(0, 0, 100, 16), Minimum = 0, Maximum = 100, Value = 0, LargeChange = 10 };
        var form = ShowWith(scrollBar);
        var events = new List<ScrollEventType>();
        scrollBar.Scroll += (_, e) => events.Add(e.Type);

        // The thumb starts at the left end of the track, roughly x=16..24.
        form.Host!.MouseDown(new Avalonia.Point(18, 8), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(60, 8));
        form.Host!.MouseUp(new Avalonia.Point(60, 8), MouseButton.Left);

        Assert.Contains(ScrollEventType.ThumbTrack, events);
        Assert.Equal(ScrollEventType.ThumbPosition, events[^2]);
        Assert.Equal(ScrollEventType.EndScroll, events[^1]);
        Assert.True(scrollBar.Value > 0, $"Expected dragging right to raise the value, got {scrollBar.Value}.");

        form.Close();
    }

    [AvaloniaFact]
    public void Home_and_End_jump_a_focused_scroll_bar_to_its_extremes()
    {
        var scrollBar = new HScrollBar { Bounds = new Rectangle(0, 0, 100, 16), Minimum = 0, Maximum = 100, Value = 50, LargeChange = 10 };
        var form = ShowWith(scrollBar);
        scrollBar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal(91, scrollBar.Value); // Maximum - LargeChange + 1, the reachable end.

        form.Host!.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(0, scrollBar.Value);

        form.Close();
    }

    // --- ScrollBar: classic look ---

    [Fact]
    public void A_scroll_bars_track_is_dithered_between_two_colours()
    {
        // The thumb sits at the left end of the track when Value is Minimum, so the
        // right end of the track (sampled here) stays the bare dithered pattern.
        var scrollBar = new HScrollBar { Bounds = new Rectangle(0, 0, 100, 16), Minimum = 0, Maximum = 100, Value = 0 };
        using var bitmap = Draw(scrollBar);

        // The classic track alternates ControlLightLight and Control pixel by pixel.
        Assert.NotEqual(bitmap.GetPixel(70, 8).ToArgb(), bitmap.GetPixel(71, 8).ToArgb());
    }

    // --- ProgressBar: Minimum/Maximum/Value/Step/PerformStep/Increment ---

    [Fact]
    public void A_progress_bars_Value_outside_Minimum_and_Maximum_throws()
    {
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() => progressBar.Value = 11);
    }

    [Fact]
    public void PerformStep_advances_by_Step_and_stops_at_Maximum()
    {
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 10, Step = 7, Value = 0 };

        progressBar.PerformStep();
        Assert.Equal(7, progressBar.Value);

        progressBar.PerformStep();
        Assert.Equal(10, progressBar.Value); // Clamped, not 14.
    }

    [Fact]
    public void Increment_clamps_to_both_ends_of_the_range()
    {
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 10, Value = 2 };

        progressBar.Increment(-20);
        Assert.Equal(0, progressBar.Value);

        progressBar.Increment(20);
        Assert.Equal(10, progressBar.Value);
    }

    // --- ProgressBar: classic drawing, pixels of the filled portion ---

    [Fact]
    public void Blocks_style_paints_segments_in_the_highlight_colour_with_gaps_between_them()
    {
        var progressBar = new ProgressBar { Size = new Size(100, 20), Minimum = 0, Maximum = 100, Value = 50, Style = ProgressBarStyle.Blocks };
        using var bitmap = Draw(progressBar);

        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(5, 10).ToArgb());     // inside the first block
        Assert.NotEqual(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(11, 10).ToArgb()); // the gap after it
        Assert.NotEqual(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(90, 10).ToArgb()); // past the filled half
    }

    [Fact]
    public void Continuous_style_paints_one_solid_bar_with_no_gaps()
    {
        var progressBar = new ProgressBar { Size = new Size(100, 20), Minimum = 0, Maximum = 100, Value = 50, Style = ProgressBarStyle.Continuous };
        using var bitmap = Draw(progressBar);

        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(5, 10).ToArgb());
        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(40, 10).ToArgb()); // Blocks would have a gap near here.
        Assert.NotEqual(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(90, 10).ToArgb());
    }

    [AvaloniaFact]
    public void Marquee_style_animates_its_sliding_block_while_the_bar_is_shown()
    {
        var progressBar = new ProgressBar { Size = new Size(100, 20), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 10 };
        var form = ShowWith(progressBar);
        using var before = Draw(progressBar);

        using var loop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ticks = 0;
        using var pump = new System.Windows.Forms.Timer { Interval = 10 };
        pump.Tick += (_, _) =>
        {
            ticks++;
            if (ticks >= 3) loop.Cancel();
        };
        pump.Start();

        Dispatcher.UIThread.MainLoop(loop.Token);

        using var after = Draw(progressBar);
        Assert.NotEqual(before.GetPixel(10, 10).ToArgb(), after.GetPixel(10, 10).ToArgb());

        form.Close();
    }

    // --- TrackBar: Minimum/Maximum/Value and keyboard/mouse ---

    [Fact]
    public void A_track_bars_Value_outside_Minimum_and_Maximum_throws()
    {
        var trackBar = new TrackBar { Minimum = 0, Maximum = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() => trackBar.Value = 11);
    }

    [AvaloniaFact]
    public void Arrow_keys_step_a_focused_track_bar_and_raise_Scroll_and_ValueChanged()
    {
        var trackBar = new TrackBar { Bounds = new Rectangle(0, 0, 104, 45), Minimum = 0, Maximum = 10, Value = 5, SmallChange = 1 };
        var form = ShowWith(trackBar);
        trackBar.Focus();
        var scrolls = 0;
        var changes = new List<int>();
        trackBar.Scroll += (_, _) => scrolls++;
        trackBar.ValueChanged += (_, _) => changes.Add(trackBar.Value);

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(6, trackBar.Value);
        Assert.Equal(1, scrolls);
        Assert.Equal([6], changes);

        form.Close();
    }

    [AvaloniaFact]
    public void Home_and_End_jump_a_track_bar_to_its_extremes()
    {
        var trackBar = new TrackBar { Bounds = new Rectangle(0, 0, 104, 45), Minimum = 0, Maximum = 10, Value = 5 };
        var form = ShowWith(trackBar);
        trackBar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal(10, trackBar.Value);

        form.Host!.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(0, trackBar.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_channel_moves_the_thumb_to_that_position()
    {
        var trackBar = new TrackBar { Bounds = new Rectangle(0, 0, 104, 45), Minimum = 0, Maximum = 10, Value = 0 };
        var form = ShowWith(trackBar);

        form.Host!.MouseDown(new Avalonia.Point(100, 22), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(100, 22), MouseButton.Left);

        Assert.True(trackBar.Value > 5, $"Expected a high value from a click near the right edge, got {trackBar.Value}.");

        form.Close();
    }

    // --- TrackBar: classic channel, thumb and ticks, proved by pixels ---

    [Fact]
    public void The_thumb_paints_in_a_different_place_at_each_extreme_of_the_range()
    {
        var trackBar = new TrackBar { Bounds = new Rectangle(0, 0, 104, 45), Minimum = 0, Maximum = 10, Value = 0 };
        using var atMinimum = Draw(trackBar);

        trackBar.Value = 10;
        using var atMaximum = Draw(trackBar);

        // Sampling near the left edge of the channel: only the thumb at Value 0 covers it.
        Assert.NotEqual(atMinimum.GetPixel(4, 20).ToArgb(), atMaximum.GetPixel(4, 20).ToArgb());
    }

    // --- PictureBox: SizeMode placement, proved by pixels ---

    [Fact]
    public void SizeMode_Normal_draws_the_image_at_its_own_size_in_the_top_left()
    {
        var image = SolidImage(20, 10, Color.Red);
        var pictureBox = new PictureBox { Size = new Size(60, 40), Image = image, SizeMode = PictureBoxSizeMode.Normal };
        using var bitmap = Draw(pictureBox);

        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
        Assert.NotEqual(Color.Red.ToArgb(), bitmap.GetPixel(50, 30).ToArgb());
    }

    [Fact]
    public void SizeMode_StretchImage_fills_the_whole_control()
    {
        var image = SolidImage(10, 10, Color.Blue);
        var pictureBox = new PictureBox { Size = new Size(60, 40), Image = image, SizeMode = PictureBoxSizeMode.StretchImage };
        using var bitmap = Draw(pictureBox);

        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(55, 35).ToArgb());
    }

    [Fact]
    public void SizeMode_CenterImage_centres_a_smaller_image_without_scaling_it()
    {
        var image = SolidImage(10, 10, Color.Green);
        var pictureBox = new PictureBox { Size = new Size(60, 40), Image = image, SizeMode = PictureBoxSizeMode.CenterImage };
        using var bitmap = Draw(pictureBox);

        Assert.Equal(Color.Green.ToArgb(), bitmap.GetPixel(30, 20).ToArgb());
        Assert.NotEqual(Color.Green.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void SizeMode_Zoom_scales_the_image_to_fit_while_keeping_its_proportions()
    {
        var image = SolidImage(10, 20, Color.Yellow); // Twice as tall as it is wide.
        var pictureBox = new PictureBox { Size = new Size(100, 40), Image = image, SizeMode = PictureBoxSizeMode.Zoom };
        using var bitmap = Draw(pictureBox);

        // Scaled to fit 100x40 at the same 1:2 ratio, the image becomes 20x40, centred horizontally.
        Assert.Equal(Color.Yellow.ToArgb(), bitmap.GetPixel(50, 20).ToArgb());
        Assert.NotEqual(Color.Yellow.ToArgb(), bitmap.GetPixel(5, 20).ToArgb());
    }

    [Fact]
    public void SizeMode_AutoSize_grows_the_control_to_the_images_size()
    {
        var image = SolidImage(33, 17, Color.Purple);
        var pictureBox = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize };

        pictureBox.Image = image;

        Assert.Equal(new Size(33, 17), pictureBox.Size);
    }

    [Fact]
    public void Load_reads_a_local_file_and_Load_without_a_location_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"basalt-picturebox-{Guid.NewGuid():N}.bmp");

        try
        {
            using (var source = SolidImage(4, 4, Color.OrangeRed))
            {
                source.Save(path);
            }

            var pictureBox = new PictureBox();
            Assert.Throws<InvalidOperationException>(() => pictureBox.Load());

            pictureBox.Load(path);

            Assert.NotNull(pictureBox.Image);
            Assert.Equal(path, pictureBox.ImageLocation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_from_a_non_file_URL_is_not_supported()
    {
        var pictureBox = new PictureBox();

        Assert.Throws<PlatformNotSupportedException>(() => pictureBox.Load("https://example.invalid/picture.png"));
    }

    [AvaloniaFact]
    public void A_held_scroll_arrow_stops_repeating_when_the_capture_is_lost()
    {
        var bar = new VScrollBar { Bounds = new System.Drawing.Rectangle(0, 0, 17, 150), Maximum = 1000 };
        var form = new Form { ClientSize = new System.Drawing.Size(100, 160) };
        form.Controls.Add(bar);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        form.Host!.MouseDown(new Avalonia.Point(8, 145), Avalonia.Input.MouseButton.Left);
        var afterPress = bar.Value;
        bar.Capture = false;

        using var loop = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        Dispatcher.UIThread.MainLoop(loop.Token);

        Assert.Equal(afterPress, bar.Value);

        form.Close();
    }
}
