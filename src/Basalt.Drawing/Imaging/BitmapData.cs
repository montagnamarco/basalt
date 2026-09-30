namespace System.Drawing.Imaging;

/// <summary>
/// The rectangle of pixels a <see cref="Bitmap.LockBits(Rectangle, ImageLockMode, PixelFormat)"/>
/// call hands out: a pointer, its stride and the format the bytes are in.
/// </summary>
public sealed class BitmapData
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int Stride { get; set; }

    public PixelFormat PixelFormat { get; set; }

    public IntPtr Scan0 { get; set; }

    public int Reserved { get; set; }
}
