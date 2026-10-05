using System.Drawing;

namespace System.Windows.Forms.VisualStyles;

public sealed partial class VisualStyleRenderer
{
    // These members are part of the binary contract even when no theme backend is active.
    // Construction rejects that state; defensive checks also cover reflection-created instances.
    public string Class => throw NotSupported();
    public IntPtr Handle => throw NotSupported();
    public int LastHResult => throw NotSupported();
    public int Part => throw NotSupported();
    public int State => throw NotSupported();

    public void DrawBackground(IDeviceContext dc, Rectangle bounds) => throw NotSupported();
    public void DrawBackground(IDeviceContext dc, Rectangle bounds, Rectangle clipRectangle) => throw NotSupported();
    public Rectangle DrawEdge(IDeviceContext dc, Rectangle bounds, Edges edges, EdgeStyle style, EdgeEffects effects) => throw NotSupported();
    public void DrawImage(Graphics graphics, Rectangle bounds, Image image) => throw NotSupported();
    public void DrawImage(Graphics graphics, Rectangle bounds, ImageList imageList, int imageIndex) => throw NotSupported();
    public void DrawParentBackground(IDeviceContext dc, Rectangle bounds, Control childControl) => throw NotSupported();
    public void DrawText(IDeviceContext dc, Rectangle bounds, string textToDraw) => throw NotSupported();
    public void DrawText(IDeviceContext dc, Rectangle bounds, string textToDraw, bool drawDisabled) => throw NotSupported();
    public void DrawText(IDeviceContext dc, Rectangle bounds, string textToDraw, bool drawDisabled, TextFormatFlags flags) => throw NotSupported();
    public Rectangle GetBackgroundContentRectangle(IDeviceContext dc, Rectangle bounds) => throw NotSupported();
    public Rectangle GetBackgroundExtent(IDeviceContext dc, Rectangle contentBounds) => throw NotSupported();
    public Region GetBackgroundRegion(IDeviceContext dc, Rectangle bounds) => throw NotSupported();
    public bool GetBoolean(BooleanProperty prop) => throw NotSupported();
    public Color GetColor(ColorProperty prop) => throw NotSupported();
    public int GetEnumValue(EnumProperty prop) => throw NotSupported();
    public string GetFilename(FilenameProperty prop) => throw NotSupported();
    public Font GetFont(IDeviceContext dc, FontProperty prop) => throw NotSupported();
    public int GetInteger(IntegerProperty prop) => throw NotSupported();
    public Padding GetMargins(IDeviceContext dc, MarginProperty prop) => throw NotSupported();
    public Size GetPartSize(IDeviceContext dc, Rectangle bounds, ThemeSizeType type) => throw NotSupported();
    public Size GetPartSize(IDeviceContext dc, ThemeSizeType type) => throw NotSupported();
    public Point GetPoint(PointProperty prop) => throw NotSupported();
    public string GetString(StringProperty prop) => throw NotSupported();
    public Rectangle GetTextExtent(IDeviceContext dc, Rectangle bounds, string textToDraw, TextFormatFlags flags) => throw NotSupported();
    public Rectangle GetTextExtent(IDeviceContext dc, string textToDraw, TextFormatFlags flags) => throw NotSupported();
    public TextMetrics GetTextMetrics(IDeviceContext dc) => throw NotSupported();
    public HitTestCode HitTestBackground(Graphics graphics, Rectangle bounds, Region region, Point point, HitTestOptions options) => throw NotSupported();
    public HitTestCode HitTestBackground(IDeviceContext dc, Rectangle bounds, Point point, HitTestOptions options) => throw NotSupported();
    public HitTestCode HitTestBackground(IDeviceContext dc, Rectangle bounds, IntPtr hRgn, Point point, HitTestOptions options) => throw NotSupported();
    public bool IsBackgroundPartiallyTransparent() => throw NotSupported();
    public void SetParameters(string className, int part, int state) => throw NotSupported();
    public void SetParameters(VisualStyleElement element) => throw NotSupported();
}
