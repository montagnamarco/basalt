using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Paints a <see cref="ToolStrip"/> and its items. The base class only
/// raises the matching Render* event; <see cref="ToolStripSystemRenderer"/>
/// and <see cref="ToolStripProfessionalRenderer"/> do the actual drawing.
/// </summary>
public abstract partial class ToolStripRenderer
{
    /// <summary>Kept for parity with Microsoft's surface; this project draws arrows without an offset table.</summary>
    protected static int Offset2X;

    protected static int Offset2Y;

    protected ToolStripRenderer()
    {
    }

    public event ToolStripArrowRenderEventHandler? RenderArrow;

    public event ToolStripItemRenderEventHandler? RenderButtonBackground;

    public event ToolStripItemRenderEventHandler? RenderDropDownButtonBackground;

    public event ToolStripGripRenderEventHandler? RenderGrip;

    public event ToolStripRenderEventHandler? RenderImageMargin;

    public event ToolStripItemRenderEventHandler? RenderItemBackground;

    public event ToolStripItemImageRenderEventHandler? RenderItemCheck;

    public event ToolStripItemImageRenderEventHandler? RenderItemImage;

    public event ToolStripItemTextRenderEventHandler? RenderItemText;

    public event ToolStripItemRenderEventHandler? RenderLabelBackground;

    public event ToolStripItemRenderEventHandler? RenderMenuItemBackground;

    public event ToolStripItemRenderEventHandler? RenderOverflowButtonBackground;

    public event ToolStripSeparatorRenderEventHandler? RenderSeparator;

    public event ToolStripItemRenderEventHandler? RenderSplitButtonBackground;

    public event ToolStripRenderEventHandler? RenderStatusStripSizingGrip;

    public event ToolStripRenderEventHandler? RenderToolStripBackground;

    public event ToolStripRenderEventHandler? RenderToolStripBorder;

    public event ToolStripItemRenderEventHandler? RenderToolStripStatusLabelBackground;

    /// <summary>A washed-out copy: Microsoft's own disabled-image look, a grey ghost of the original.</summary>
    public static Image CreateDisabledImage(Image normalImage)
    {
        ArgumentNullException.ThrowIfNull(normalImage);

        var bitmap = new Bitmap(Math.Max(normalImage.Width, 1), Math.Max(normalImage.Height, 1));

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(normalImage, 0, 0, normalImage.Width, normalImage.Height);
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0) continue;

                // Grey, then faded toward the control face, as a disabled glyph looks washed out.
                var grey = (pixel.R + pixel.G + pixel.B) / 3;
                var faded = (grey + SystemColors.Control.R) / 2;
                bitmap.SetPixel(x, y, Color.FromArgb(pixel.A / 2, faded, faded, faded));
            }
        }

        return bitmap;
    }

    public void DrawArrow(ToolStripArrowRenderEventArgs e) => OnRenderArrow(e);

    public void DrawButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

    public void DrawDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderDropDownButtonBackground(e);

    public void DrawGrip(ToolStripGripRenderEventArgs e) => OnRenderGrip(e);

    public void DrawImageMargin(ToolStripRenderEventArgs e) => OnRenderImageMargin(e);

    public void DrawItemBackground(ToolStripItemRenderEventArgs e) => OnRenderItemBackground(e);

    public void DrawItemCheck(ToolStripItemImageRenderEventArgs e) => OnRenderItemCheck(e);

    public void DrawItemImage(ToolStripItemImageRenderEventArgs e) => OnRenderItemImage(e);

    public void DrawItemText(ToolStripItemTextRenderEventArgs e) => OnRenderItemText(e);

    public void DrawLabelBackground(ToolStripItemRenderEventArgs e) => OnRenderLabelBackground(e);

    public void DrawMenuItemBackground(ToolStripItemRenderEventArgs e) => OnRenderMenuItemBackground(e);

    public void DrawOverflowButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderOverflowButtonBackground(e);

    public void DrawSeparator(ToolStripSeparatorRenderEventArgs e) => OnRenderSeparator(e);

    public void DrawSplitButton(ToolStripItemRenderEventArgs e) => OnRenderSplitButtonBackground(e);

    public void DrawStatusStripSizingGrip(ToolStripRenderEventArgs e) => OnRenderStatusStripSizingGrip(e);

    public void DrawToolStripBackground(ToolStripRenderEventArgs e) => OnRenderToolStripBackground(e);

    public void DrawToolStripBorder(ToolStripRenderEventArgs e) => OnRenderToolStripBorder(e);

    public void DrawToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e) => OnRenderToolStripStatusLabelBackground(e);

    protected virtual void Initialize(ToolStrip toolStrip)
    {
    }

    /// <summary>Lets ToolStrip.OnHandleCreated (a different class) call the protected hook above.</summary>
    internal void InitializeOnHandleCreated(ToolStrip toolStrip) => Initialize(toolStrip);

    protected virtual void InitializeItem(ToolStripItem item)
    {
    }

    protected virtual void OnRenderArrow(ToolStripArrowRenderEventArgs e) => RenderArrow?.Invoke(this, e);

    protected virtual void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => RenderButtonBackground?.Invoke(this, e);

    protected virtual void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => RenderDropDownButtonBackground?.Invoke(this, e);

    protected virtual void OnRenderGrip(ToolStripGripRenderEventArgs e) => RenderGrip?.Invoke(this, e);

    protected virtual void OnRenderImageMargin(ToolStripRenderEventArgs e) => RenderImageMargin?.Invoke(this, e);

    protected virtual void OnRenderItemBackground(ToolStripItemRenderEventArgs e) => RenderItemBackground?.Invoke(this, e);

    /// <summary>The classic default: a sunken square in the image margin, with a check mark glyph inside.</summary>
    protected virtual void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        RenderItemCheck?.Invoke(this, e);

        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var face = new SolidBrush(SystemColors.ControlLight);
        e.Graphics.FillRectangle(face, bounds);
        ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);
        ControlPaint.DrawMenuGlyph(e.Graphics, Rectangle.Inflate(bounds, -2, -2), MenuGlyph.Checkmark, SystemColors.ControlText, Color.Empty);
    }

    /// <summary>The item's own image, drawn at the rectangle the layout (or an item's own paint) worked out.</summary>
    protected virtual void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        RenderItemImage?.Invoke(this, e);

        if (e.Image is null || e.ImageRectangle.Width <= 0 || e.ImageRectangle.Height <= 0) return;

        if (e.Item.Enabled) e.Graphics.DrawImage(e.Image, e.ImageRectangle);
        else ControlPaint.DrawImageDisabled(e.Graphics, e.Image, e.ImageRectangle.Left, e.ImageRectangle.Top, SystemColors.Control);
    }

    /// <summary>The item's own caption, in its own colour and font.</summary>
    protected virtual void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        RenderItemText?.Invoke(this, e);

        if (string.IsNullOrEmpty(e.Text) || e.TextRectangle.Width <= 0 || e.TextRectangle.Height <= 0) return;

        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont ?? e.Item.Font, e.TextRectangle, e.TextColor, e.TextFormat);
    }

    protected virtual void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) => RenderLabelBackground?.Invoke(this, e);

    protected virtual void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => RenderMenuItemBackground?.Invoke(this, e);

    protected virtual void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e) => RenderOverflowButtonBackground?.Invoke(this, e);

    protected virtual void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) => RenderSeparator?.Invoke(this, e);

    protected virtual void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => RenderSplitButtonBackground?.Invoke(this, e);

    protected virtual void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) => RenderStatusStripSizingGrip?.Invoke(this, e);

    protected virtual void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => RenderToolStripBackground?.Invoke(this, e);

    protected virtual void OnRenderToolStripBorder(ToolStripRenderEventArgs e) => RenderToolStripBorder?.Invoke(this, e);

    protected virtual void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e) => RenderToolStripStatusLabelBackground?.Invoke(this, e);

    /// <summary>No-op in this project: there is one DPI-independent classic theme, so there is nothing to rescale.</summary>
    protected static void ScaleArrowOffsetsIfNeeded()
    {
    }

    protected static void ScaleArrowOffsetsIfNeeded(int dpi)
    {
    }
}
