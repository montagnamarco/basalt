namespace Basalt.Workspace;

/// <summary>
/// Encodes and decodes the .resx representation Visual Studio writes for an
/// embedded Image or Icon: the raw file bytes, base64 encoded, tagged with the
/// runtime type and the "bytearray" mimetype that <c>ImageConverter</c> and
/// <c>Icon</c>'s own stream constructor read back directly. No BinaryFormatter
/// or other arbitrary deserialization is ever involved, so decoding stays safe
/// for the designer's preview to run unattended.
/// </summary>
public static class WinFormsImageResources
{
    public const string ByteArrayMimeType = "application/x-microsoft.net.object.bytearray.base64";
    public const string BitmapTypeName = "System.Drawing.Bitmap, System.Drawing";
    public const string IconTypeName = "System.Drawing.Icon, System.Drawing";

    /// <summary>Builds the resx value, type and mimetype for a freshly imported image.</summary>
    public static (string Value, string TypeName, string MimeType) Encode(byte[] bytes, bool isIcon)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return (Convert.ToBase64String(bytes), isIcon ? IconTypeName : BitmapTypeName, ByteArrayMimeType);
    }

    /// <summary>
    /// Reads the bytes back from an entry written this way, whether it was
    /// imported by the designer or by Visual Studio itself: both use the same
    /// type and mimetype pair.
    /// </summary>
    public static bool TryDecode(WinFormsResourceEntry entry, out byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bytes = [];
        if (entry.MimeType != ByteArrayMimeType || !IsImageTypeName(entry.TypeName))
        {
            return false;
        }

        try
        {
            // Convert.FromBase64String skips embedded whitespace, which is how
            // Visual Studio wraps the value across several indented lines.
            bytes = Convert.FromBase64String(entry.Value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Whether a resx "type" attribute names Image, Bitmap or Icon, ignoring its assembly.</summary>
    public static bool IsImageTypeName(string? typeName)
    {
        if (typeName is null) return false;
        var separator = typeName.IndexOf(',');
        var declared = (separator < 0 ? typeName : typeName[..separator]).Trim();
        return declared is "System.Drawing.Bitmap" or "System.Drawing.Icon" or "System.Drawing.Image";
    }
}
