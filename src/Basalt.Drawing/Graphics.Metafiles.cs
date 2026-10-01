using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace System.Drawing;

/// <summary>Enumerating a metafile's records, and the one way to add to a metafile this library
/// does not support: recording.</summary>
/// <remarks>
/// <see cref="EnumerateMetafile"/> walks the records a <see cref="Metafile"/> already parsed at
/// construction and calls <paramref name="callback"/> once per record, passing a copy of its bytes
/// in unmanaged memory -- exactly what GDI+ hands a callback, since the real EMF/WMF record is a
/// byte blob with no object identity of its own. The placement and size parameters these overloads
/// take (a point, a rectangle, an array of points, a device context, image attributes) only matter
/// to GDI+ when it is also drawing the metafile as it enumerates it; this library only enumerates,
/// so they are accepted for signature compatibility and otherwise unused.
/// </remarks>
public sealed partial class Graphics
{
    /// <summary>Called by <see cref="EnumerateMetafile"/> for each record; returning false stops
    /// the enumeration early, as GDI+'s own callback does.</summary>
    public delegate bool EnumerateMetafileProc(EmfPlusRecordType recordType, int flags, int dataSize, IntPtr data, PlayRecordCallback? callbackData);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, PointF[] destPoints, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Point[] destPoints, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, Rectangle destRect, Rectangle srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData) =>
        EnumerateMetafileCore(metafile, callback);

    public void EnumerateMetafile(Metafile metafile, RectangleF destRect, RectangleF srcRect, GraphicsUnit unit, EnumerateMetafileProc callback, IntPtr callbackData, ImageAttributes? imageAttr) =>
        EnumerateMetafileCore(metafile, callback);

    /// <summary>Appends a comment record to a metafile being recorded.</summary>
    /// <exception cref="NotImplementedException">Always: recording a metafile is not implemented,
    /// so there is never a comment stream to append to.</exception>
    public void AddMetafileComment(byte[] data) =>
        throw new NotImplementedException("recording metafiles is not implemented");

    // Walks the records a Metafile already parsed, handing each one's raw type and a copy of its
    // bytes -- in unmanaged memory, as GDI+ does -- to the caller's callback. The copy is freed
    // right after the callback returns, so a callback that kept the pointer past its own call would
    // be reading freed memory under real GDI+ too.
    private void EnumerateMetafileCore(Metafile metafile, EnumerateMetafileProc callback)
    {
        ArgumentNullException.ThrowIfNull(metafile);
        ArgumentNullException.ThrowIfNull(callback);

        foreach (var (type, data) in metafile.EnumerateRecords())
        {
            var handle = Marshal.AllocHGlobal(Math.Max(data.Length, 1));

            try
            {
                if (data.Length > 0) Marshal.Copy(data, 0, handle, data.Length);

                if (!callback(type, 0, data.Length, handle, null)) break;
            }
            finally
            {
                Marshal.FreeHGlobal(handle);
            }
        }
    }
}
