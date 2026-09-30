namespace System.Drawing.Drawing2D;

/// <summary>A region written out as bytes, to be read back by a Region.</summary>
public sealed class RegionData
{
    internal RegionData()
    {
    }

    public byte[] Data { get; set; } = [];
}
