namespace System.Drawing.Printing;

/// <summary>One rendered page a PreviewPrintController collected, as an Image.</summary>
public sealed class PreviewPageInfo
{
    public PreviewPageInfo(Image image, Size physicalSize)
    {
        ArgumentNullException.ThrowIfNull(image);

        Image = image;
        PhysicalSize = physicalSize;
    }

    public Image Image { get; }

    public Size PhysicalSize { get; }
}
