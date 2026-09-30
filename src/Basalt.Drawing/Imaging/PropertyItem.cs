namespace System.Drawing.Imaging;

/// <summary>
/// One tag of the metadata a decoded image carried (EXIF and the like), kept
/// on <see cref="Image"/> and read back through
/// <see cref="Image.GetPropertyItem(int)"/>.
/// </summary>
/// <remarks>
/// GDI+ never gave this class a public constructor: code that wants to build
/// one from scratch has always had to go through reflection. This assembly
/// keeps that same friction rather than adding a member Microsoft does not have.
/// </remarks>
public sealed class PropertyItem
{
    internal PropertyItem()
    {
    }

    public int Id { get; set; }

    public int Len { get; set; }

    public short Type { get; set; }

    public byte[] Value { get; set; } = [];
}
