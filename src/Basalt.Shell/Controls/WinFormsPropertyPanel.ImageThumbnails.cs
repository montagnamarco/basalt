using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsPropertyPanel
{
    private const int ThumbnailSize = 16;

    /// <summary>
    /// Each picture's thumbnail, made once: the grid is rebuilt after every edit
    /// on the form, and re-encoding a large image each time would make an
    /// unrelated rename or move wait for it. Keyed weakly by the picture
    /// itself, so a replaced image takes its thumbnail with it.
    /// </summary>
    private static readonly ConditionalWeakTable<object, byte[]> Thumbnails = new();

    /// <summary>
    /// A 16x16 preview of an Image, Bitmap or Icon property's current value, the way
    /// Visual Studio's property grid shows one beside the value text. Null for every
    /// other property, and for an unset picture property.
    /// </summary>
    /// <remarks>
    /// Computed here rather than in <c>Basalt.Designer</c>: that project does not
    /// reference the assembly real <c>System.Drawing.Icon</c>/<c>Image</c> types are
    /// forwarded from, while this one already does (it edits these same properties
    /// through <see cref="WinFormsImageResourceDialog"/>).
    /// </remarks>
    private static byte[]? ImageThumbnailFor(object control, DesignableProperty property)
    {
        if (!WinFormsDesignerView.IsImageProperty(property.ValueType)) return null;

        var descriptor = TypeDescriptor.GetProperties(control)[property.Name];
        if (descriptor is null) return null;

        object? value;
        try
        {
            value = descriptor.GetValue(control);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException)
        {
            // Mirrors WinFormsPropertyInspector.ReadDesignerValue: a getter that
            // depends on runtime state (a closed SerialPort, say) must not crash a
            // thumbnail that is only a convenience.
            return null;
        }

        if (value is not (Icon or Image)) return null;

        if (Thumbnails.TryGetValue(value, out var cached)) return cached;

        try
        {
            var thumbnail = EncodeThumbnail(value);
            Thumbnails.AddOrUpdate(value, thumbnail);
            return thumbnail;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException
                                              or ObjectDisposedException or InvalidOperationException)
        {
            // An already-corrupt or disposed image (see WinFormsImageResourceDialog's
            // own decode check on import) is shown without a thumbnail rather than
            // breaking the grid.
            return null;
        }
    }

    /// <summary>
    /// The picture scaled down to fit 16x16, as PNG. Scaled before encoding, so a
    /// photo costs a 16x16 encode rather than one at its full size.
    /// </summary>
    private static byte[] EncodeThumbnail(object value)
    {
        // Icon.Save writes the .ico container, which Avalonia's own bitmap decoder
        // does not read back; ToBitmap gives a picture every decoder understands.
        using var icon = value is Icon source ? source.ToBitmap() : null;
        var image = icon ?? (Image)value;

        var scale = Math.Min(1.0, Math.Min((double)ThumbnailSize / image.Width, (double)ThumbnailSize / image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var small = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(small))
            graphics.DrawImage(image, 0, 0, width, height);

        using var stream = new MemoryStream();
        small.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
