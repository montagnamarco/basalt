namespace System.Drawing.Imaging;

/// <summary>Describes a metafile: its kind, size, resolution and the bounds it was recorded against.</summary>
/// <remarks>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/MetafileTypes.cs), MIT licensed,
/// copyright Jonathan Pobst and Peter Gill (Majorsilence). The values are
/// filled by <see cref="Metafile.GetMetafileHeader(string)"/> by reading the
/// EMF or placeable-WMF header bytes directly; nothing here calls GDI.
/// </remarks>
public sealed class MetafileHeader
{
    internal MetafileHeader()
    {
    }

    /// <summary>The kind of metafile this describes.</summary>
    public MetafileType Type { get; internal set; } = MetafileType.Invalid;

    /// <summary>The size of the metafile, in bytes.</summary>
    public int MetafileSize { get; internal set; }

    /// <summary>The format version.</summary>
    public int Version { get; internal set; }

    /// <summary>The horizontal resolution the metafile was recorded at, in dots per inch.</summary>
    public float DpiX { get; internal set; }

    /// <summary>The vertical resolution the metafile was recorded at, in dots per inch.</summary>
    public float DpiY { get; internal set; }

    /// <summary>The rectangle the metafile was recorded against, in its own units.</summary>
    public Rectangle Bounds { get; internal set; }

    /// <summary>The WMF header, when this describes a Windows metafile; otherwise null.</summary>
    public MetaHeader? WmfHeader { get; internal set; }

    /// <summary>The size of the EMF+ header, in bytes; zero outside EMF+.</summary>
    public int EmfPlusHeaderSize { get; internal set; }

    /// <summary>The horizontal resolution the metafile was recorded at.</summary>
    public int LogicalDpiX { get; internal set; }

    /// <summary>The vertical resolution the metafile was recorded at.</summary>
    public int LogicalDpiY { get; internal set; }

    /// <summary>Whether this describes a Windows metafile, placeable or not.</summary>
    public bool IsWmf() => Type is MetafileType.Wmf or MetafileType.WmfPlaceable;

    /// <summary>Whether this describes a placeable Windows metafile.</summary>
    public bool IsWmfPlaceable() => Type == MetafileType.WmfPlaceable;

    /// <summary>Whether this describes a plain enhanced metafile.</summary>
    public bool IsEmf() => Type == MetafileType.Emf;

    /// <summary>Whether this describes an enhanced metafile of any flavour, including EMF+.</summary>
    public bool IsEmfOrEmfPlus() => Type is MetafileType.Emf or MetafileType.EmfPlusOnly or MetafileType.EmfPlusDual;

    /// <summary>Whether this describes an EMF+ metafile, dual or EMF+-only.</summary>
    public bool IsEmfPlus() => Type is MetafileType.EmfPlusOnly or MetafileType.EmfPlusDual;

    /// <summary>Whether this describes an EMF+ metafile that also carries EMF records.</summary>
    public bool IsEmfPlusDual() => Type == MetafileType.EmfPlusDual;

    /// <summary>Whether this describes an EMF+ metafile holding only EMF+ records.</summary>
    public bool IsEmfPlusOnly() => Type == MetafileType.EmfPlusOnly;

    /// <summary>Whether the metafile was recorded against a display rather than a printer.</summary>
    public bool IsDisplay() => IsEmfPlus();
}
