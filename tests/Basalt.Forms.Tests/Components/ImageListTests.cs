using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Components;

/// <summary>
/// ImageList: pictures resized to a shared size, an optional colour key, keys
/// alongside the index, and drawing read back pixel by pixel.
/// </summary>
public class ImageListTests
{
    private static Bitmap Solid(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);

        return bitmap;
    }

    [Fact]
    public void A_picture_added_is_stored_resized_to_ImageSize()
    {
        using var list = new ImageList();
        using var picture = Solid(32, 48, Color.Blue);

        list.Images.Add(picture);

        Assert.Equal(new Size(16, 16), list.ImageSize);
        using var stored = list.Images[0];
        Assert.Equal(new Size(16, 16), stored.Size);
    }

    [Fact]
    public void Changing_ImageSize_resizes_pictures_already_stored()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Green);
        list.Images.Add(picture);

        list.ImageSize = new Size(32, 32);

        using var stored = list.Images[0];
        Assert.Equal(new Size(32, 32), stored.Size);
    }

    [Fact]
    public void A_picture_is_made_transparent_where_it_matches_TransparentColor()
    {
        using var list = new ImageList { TransparentColor = Color.Magenta };
        using var picture = Solid(16, 16, Color.Magenta);

        list.Images.Add(picture);

        using var stored = list.Images[0];
        var pixel = ((Bitmap)stored).GetPixel(8, 8);
        Assert.Equal(0, pixel.A);
    }

    [Fact]
    public void A_picture_keeps_its_colour_when_TransparentColor_is_the_default()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Magenta);

        list.Images.Add(picture);

        using var stored = list.Images[0];
        var pixel = ((Bitmap)stored).GetPixel(8, 8);
        Assert.Equal(255, pixel.A);
        Assert.Equal(Color.Magenta.ToArgb(), Color.FromArgb(255, pixel.R, pixel.G, pixel.B).ToArgb());
    }

    [Fact]
    public void Add_with_its_own_colour_does_not_use_the_lists_TransparentColor()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Red);

        var index = list.Images.Add(picture, Color.Red);

        Assert.Equal(0, index);
        using var stored = list.Images[0];
        Assert.Equal(0, ((Bitmap)stored).GetPixel(8, 8).A);
    }

    [Fact]
    public void Keys_find_their_picture_and_are_removed_by_name()
    {
        using var list = new ImageList();
        using var first = Solid(16, 16, Color.Red);
        using var second = Solid(16, 16, Color.Blue);

        list.Images.Add("first", first);
        list.Images.Add("second", second);

        Assert.True(list.Images.ContainsKey("first"));
        Assert.Equal(1, list.Images.IndexOfKey("second"));
        Assert.False(list.Images.ContainsKey("missing"));

        list.Images.RemoveByKey("first");

        Assert.False(list.Images.ContainsKey("first"));
        Assert.Single(list.Images);
        Assert.Equal(0, list.Images.IndexOfKey("second"));
    }

    [Fact]
    public void Empty_reflects_whether_any_picture_is_stored()
    {
        using var list = new ImageList();
        Assert.True(list.Images.Empty);

        using var picture = Solid(16, 16, Color.Black);
        list.Images.Add(picture);
        Assert.False(list.Images.Empty);
    }

    [Fact]
    public void AddStrip_splits_a_filmstrip_into_separate_pictures()
    {
        using var list = new ImageList();
        using var strip = new Bitmap(48, 16, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.FillRectangle(Brushes.Red, new Rectangle(0, 0, 16, 16));
            graphics.FillRectangle(Brushes.Green, new Rectangle(16, 0, 16, 16));
            graphics.FillRectangle(Brushes.Blue, new Rectangle(32, 0, 16, 16));
        }

        var firstIndex = list.Images.AddStrip(strip);

        Assert.Equal(0, firstIndex);
        Assert.Equal(3, list.Images.Count);
        using var second = (Bitmap)list.Images[1];
        Assert.Equal(Color.Green.ToArgb(), Color.FromArgb(255, second.GetPixel(8, 8).R, second.GetPixel(8, 8).G, second.GetPixel(8, 8).B).ToArgb());
    }

    [Fact]
    public void Draw_paints_the_picture_at_its_ImageSize()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Red);
        list.Images.Add(picture);

        using var canvas = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.White);

        list.Draw(graphics, 4, 4, 0);

        var painted = canvas.GetPixel(4 + 8, 4 + 8);
        Assert.Equal(Color.Red.ToArgb(), Color.FromArgb(255, painted.R, painted.G, painted.B).ToArgb());
        Assert.Equal(Color.White.ToArgb(), canvas.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Draw_at_a_size_stretches_the_picture()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Blue);
        list.Images.Add(picture);

        using var canvas = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);

        list.Draw(graphics, 0, 0, 64, 64, 0);

        var painted = canvas.GetPixel(32, 32);
        Assert.Equal(Color.Blue.ToArgb(), Color.FromArgb(255, painted.R, painted.G, painted.B).ToArgb());
    }

    [Fact]
    public void Drawing_an_index_outside_the_collection_throws()
    {
        using var list = new ImageList();
        using var canvas = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);

        Assert.Throws<ArgumentOutOfRangeException>(() => list.Draw(graphics, 0, 0, 0));
    }

    [Fact]
    public void The_handle_is_created_on_first_use_and_stays_the_same_until_recreated()
    {
        using var list = new ImageList();

        Assert.False(list.HandleCreated);
        var handle = list.Handle;
        Assert.True(list.HandleCreated);
        Assert.Equal(handle, list.Handle);
    }

    [Fact]
    public void Changing_ImageSize_after_the_handle_exists_recreates_it()
    {
        using var list = new ImageList();
        var firstHandle = list.Handle;
        var raised = false;
        list.RecreateHandle += (_, _) => raised = true;

        list.ImageSize = new Size(32, 32);

        Assert.True(raised);
        Assert.NotEqual(firstHandle, list.Handle);
    }

    [Fact]
    public void Changing_ImageSize_before_the_handle_exists_does_not_raise_RecreateHandle()
    {
        using var list = new ImageList();
        var raised = false;
        list.RecreateHandle += (_, _) => raised = true;

        list.ImageSize = new Size(32, 32);

        Assert.False(raised);
    }

    [Fact]
    public void ImageSize_outside_1_to_256_is_rejected()
    {
        using var list = new ImageList();

        Assert.Throws<ArgumentOutOfRangeException>(() => list.ImageSize = new Size(0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.ImageSize = new Size(16, 257));
    }

    [Fact]
    public void ImageStream_round_trips_the_pictures_in_memory()
    {
        using var source = new ImageList();
        using var picture = Solid(16, 16, Color.Red);
        source.Images.Add("red", picture);

        using var target = new ImageList();
        target.ImageStream = source.ImageStream;

        Assert.Single(target.Images);
        Assert.True(target.Images.ContainsKey("red"));
        using var stored = (Bitmap)target.Images[0];
        var pixel = stored.GetPixel(8, 8);
        Assert.Equal(Color.Red.ToArgb(), Color.FromArgb(255, pixel.R, pixel.G, pixel.B).ToArgb());
    }

    [Fact]
    public void ImageStream_is_null_for_an_empty_list()
    {
        using var list = new ImageList();

        Assert.Null(list.ImageStream);
    }

    [Fact]
    public void Getting_an_image_returns_a_copy_that_does_not_alias_the_stored_one()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Red);
        list.Images.Add(picture);

        using var first = (Bitmap)list.Images[0];
        first.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));

        using var second = (Bitmap)list.Images[0];
        Assert.Equal(255, second.GetPixel(0, 0).A);
    }

    [Fact]
    public void GetObjectData_and_the_serialization_constructor_are_not_supported()
    {
        using var list = new ImageList();
        using var picture = Solid(16, 16, Color.Red);
        list.Images.Add(picture);
        using var stream = list.ImageStream!;

        Assert.Throws<PlatformNotSupportedException>(() => stream.GetObjectData(SerializationInfo(), default));
    }

    // Only a test of the obsolete-but-still-compiling ISerializable members needs these types.
#pragma warning disable SYSLIB0050
    private static System.Runtime.Serialization.SerializationInfo SerializationInfo() =>
        new(typeof(ImageListStreamer), new System.Runtime.Serialization.FormatterConverter());
#pragma warning restore SYSLIB0050
}
