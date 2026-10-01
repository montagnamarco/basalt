namespace System.Drawing.Imaging;

/// <summary>The 22-byte header a placeable Windows metafile is prefixed with.</summary>
/// <remarks>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/MetafileTypes.cs), MIT licensed,
/// copyright Jonathan Pobst and Peter Gill (Majorsilence). Plain data: GDI+
/// fills it from the bytes at the start of a placeable WMF, which this
/// assembly also reads directly in <see cref="Metafile.GetMetafileHeader(string)"/>.
/// </remarks>
public sealed class WmfPlaceableFileHeader
{
    public WmfPlaceableFileHeader()
    {
    }

    /// <summary>The magic number (0x9AC6CDD7) identifying a placeable metafile.</summary>
    public int Key { get; set; }

    /// <summary>The metafile handle; always zero once written to disk.</summary>
    public short Hmf { get; set; }

    /// <summary>The left edge of the bounding rectangle, in metafile units.</summary>
    public short BboxLeft { get; set; }

    /// <summary>The top edge of the bounding rectangle, in metafile units.</summary>
    public short BboxTop { get; set; }

    /// <summary>The right edge of the bounding rectangle, in metafile units.</summary>
    public short BboxRight { get; set; }

    /// <summary>The bottom edge of the bounding rectangle, in metafile units.</summary>
    public short BboxBottom { get; set; }

    /// <summary>The number of metafile units per inch.</summary>
    public short Inch { get; set; }

    /// <summary>Reserved; always zero.</summary>
    public int Reserved { get; set; }

    /// <summary>The checksum of the preceding fields.</summary>
    public short Checksum { get; set; }
}
