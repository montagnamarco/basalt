using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// A snapshot of a bitmap's pixels, taken once, that
/// <see cref="Graphics.DrawCachedBitmap"/> draws back one for one. GDI+
/// documents that draw as ignoring the current world transform except its
/// translation, and as obeying the clip, same as any other drawing call.
/// </summary>
public sealed class CachedBitmap : IDisposable
{
    private SKBitmap? _pixels;

    public CachedBitmap(Bitmap bitmap, Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(graphics);

        // Copied now: GDI+ documents the cache as a snapshot, unaffected by
        // whatever the source bitmap does to its own pixels afterwards.
        // The Graphics argument exists in GDI+ so the native cache can match
        // the destination device; this stand-in has only one kind of device,
        // so nothing further is read from it.
        _pixels = bitmap.Pixels.Copy();
    }

    ~CachedBitmap() => Dispose(false);

    internal SKBitmap Pixels => _pixels ?? throw new ObjectDisposedException(nameof(CachedBitmap));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;

        _pixels?.Dispose();
        _pixels = null;
    }
}
