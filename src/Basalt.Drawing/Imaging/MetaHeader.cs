namespace System.Drawing.Imaging;

/// <summary>The header of a Windows metafile (the METAHEADER structure).</summary>
/// <remarks>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/MetafileTypes.cs), MIT licensed,
/// copyright Jonathan Pobst and Peter Gill (Majorsilence). Adapted to this
/// assembly's namespace and left as plain data: nothing here needs GDI.
/// </remarks>
public sealed class MetaHeader
{
    public MetaHeader()
    {
    }

    /// <summary>Whether the metafile is in memory (1) or on disk (0).</summary>
    public short Type { get; set; }

    /// <summary>The size of the header, in 16-bit words.</summary>
    public short HeaderSize { get; set; }

    /// <summary>The format version.</summary>
    public short Version { get; set; }

    /// <summary>The size of the metafile, in 16-bit words.</summary>
    public int Size { get; set; }

    /// <summary>The number of objects the metafile defines.</summary>
    public short NoObjects { get; set; }

    /// <summary>The size of the largest record, in 16-bit words.</summary>
    public int MaxRecord { get; set; }

    /// <summary>The number of parameters, always zero; present for layout compatibility.</summary>
    public short NoParameters { get; set; }
}
