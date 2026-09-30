namespace System.Drawing.Drawing2D;

/// <summary>The points and point types of a path, as plain arrays.</summary>
public sealed class PathData
{
    public PathData()
    {
    }

    public PointF[]? Points { get; set; }

    public byte[]? Types { get; set; }
}
