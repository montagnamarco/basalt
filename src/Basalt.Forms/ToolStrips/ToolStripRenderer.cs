using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Paints a <see cref="ToolStrip"/> and its items. Public Draw methods call
/// the virtual painter before notifying Render* subscribers, so custom
/// event painting remains above the renderer's default pixels.
/// </summary>
public abstract partial class ToolStripRenderer
{
    /// <summary>Kept for parity with Microsoft's surface; this project draws arrows without an offset table.</summary>
    protected static int Offset2X;

    protected static int Offset2Y;

    /// <summary>
    /// A separator's own bounds, unchanged: for a drop-down menu, <see cref="ToolStripSeparator.SetBounds"/>
    /// already positions and sizes it to <see cref="ToolStrip.DisplayRectangle"/>, which itself starts
    /// after the image margin for a <see cref="ToolStripDropDownMenu"/> (see its own
    /// <c>DisplayRectangle</c> override). Trimming the margin again here, on top of that, pushed the
    /// etched line a second margin-width too far right, off into the row's own trailing padding, instead
    /// of right after the icon column where Windows draws it.
    /// </summary>
    internal static Rectangle SeparatorBounds(ToolStripItem item) => item.Bounds;

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

    public void DrawArrow(ToolStripArrowRenderEventArgs e)
    {
        OnRenderArrow(e);
        RenderArrow?.Invoke(this, e);
    }

    public void DrawButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderButtonBackground(e);
        RenderButtonBackground?.Invoke(this, e);
    }

    public void DrawDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderDropDownButtonBackground(e);
        RenderDropDownButtonBackground?.Invoke(this, e);
    }

    public void DrawGrip(ToolStripGripRenderEventArgs e)
    {
        OnRenderGrip(e);
        RenderGrip?.Invoke(this, e);
    }

    public void DrawImageMargin(ToolStripRenderEventArgs e)
    {
        OnRenderImageMargin(e);
        RenderImageMargin?.Invoke(this, e);
    }

    public void DrawItemBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderItemBackground(e);
        RenderItemBackground?.Invoke(this, e);
    }

    public void DrawItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        OnRenderItemCheck(e);
        RenderItemCheck?.Invoke(this, e);
    }

    public void DrawItemImage(ToolStripItemImageRenderEventArgs e)
    {
        OnRenderItemImage(e);
        RenderItemImage?.Invoke(this, e);
    }

    public void DrawItemText(ToolStripItemTextRenderEventArgs e)
    {
        OnRenderItemText(e);
        RenderItemText?.Invoke(this, e);
    }

    public void DrawLabelBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderLabelBackground(e);
        RenderLabelBackground?.Invoke(this, e);
    }

    public void DrawMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderMenuItemBackground(e);
        RenderMenuItemBackground?.Invoke(this, e);
    }

    public void DrawOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderOverflowButtonBackground(e);
        RenderOverflowButtonBackground?.Invoke(this, e);
    }

    public void DrawSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        OnRenderSeparator(e);
        RenderSeparator?.Invoke(this, e);
    }

    public void DrawSplitButton(ToolStripItemRenderEventArgs e)
    {
        OnRenderSplitButtonBackground(e);
        RenderSplitButtonBackground?.Invoke(this, e);
    }

    public void DrawStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
        OnRenderStatusStripSizingGrip(e);
        RenderStatusStripSizingGrip?.Invoke(this, e);
    }

    public void DrawToolStripBackground(ToolStripRenderEventArgs e)
    {
        OnRenderToolStripBackground(e);
        RenderToolStripBackground?.Invoke(this, e);
    }

    public void DrawToolStripBorder(ToolStripRenderEventArgs e)
    {
        OnRenderToolStripBorder(e);
        RenderToolStripBorder?.Invoke(this, e);
    }

    public void DrawToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderToolStripStatusLabelBackground(e);
        RenderToolStripStatusLabelBackground?.Invoke(this, e);
    }

    protected virtual void Initialize(ToolStrip toolStrip)
    {
    }

    /// <summary>Lets ToolStrip.OnHandleCreated (a different class) call the protected hook above.</summary>
    internal void InitializeOnHandleCreated(ToolStrip toolStrip) => Initialize(toolStrip);

    protected virtual void InitializeItem(ToolStripItem item)
    {
    }

    protected virtual void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (this is ToolStripSystemRenderer systemRenderer)
        {
            systemRenderer.PaintSystemArrow(e);
        }
    }

    protected virtual void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
    {
    }

    /// <summary>The classic default: a sunken square in the image margin, with a check mark glyph inside.</summary>
    protected virtual void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (e.ToolStrip is { PortableVisualStylesEnabled: true } strip)
        {
            var palette = Internal.Rendering.PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
            using var portableFace = new SolidBrush(e.Item.HasExplicitPortableBackColor ? e.Item.BackColor : palette.PressedFace);
            e.Graphics.FillRectangle(portableFace, bounds);
            using var border = new Pen(palette.Border);
            e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            ControlPaint.DrawMenuGlyph(e.Graphics, Rectangle.Inflate(bounds, -2, -2), MenuGlyph.Checkmark,
                e.Item.Enabled ? palette.Text : palette.DisabledText, Color.Empty);
            return;
        }

        using var face = new SolidBrush(SystemColors.ControlLight);
        e.Graphics.FillRectangle(face, bounds);
        ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);
        ControlPaint.DrawMenuGlyph(e.Graphics, Rectangle.Inflate(bounds, -2, -2), MenuGlyph.Checkmark, SystemColors.ControlText, Color.Empty);
    }

    /// <summary>The item's own image, drawn at the rectangle the layout (or an item's own paint) worked out.</summary>
    protected virtual void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is not null && e.ImageRectangle.Width > 0 && e.ImageRectangle.Height > 0)
        {
            if (e.Item.Enabled)
            {
                e.Graphics.DrawImage(e.Image, e.ImageRectangle);
            }
            else
            {
                ControlPaint.DrawImageDisabled(e.Graphics, e.Image, e.ImageRectangle.Left, e.ImageRectangle.Top, SystemColors.Control);
            }
        }
    }

    /// <summary>The item's own caption, in its own colour and font.</summary>
    protected virtual void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.ToolStrip is { PortableVisualStylesEnabled: true } strip
            && !e.Item.HasExplicitPortableForeColor && !strip.HasExplicitAppearanceForeColor
            && (e.TextColor == e.Item.ForeColor || !e.Item.Enabled && e.TextColor == SystemColors.GrayText))
        {
            var palette = Internal.Rendering.PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
            e.TextColor = e.Item.Enabled ? palette.Text : palette.DisabledText;
        }

        if (!string.IsNullOrEmpty(e.Text) && e.TextRectangle.Width > 0 && e.TextRectangle.Height > 0)
        {
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont ?? e.Item.Font, e.TextRectangle, e.TextColor, e.TextFormat);
        }
    }

    protected virtual void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
    }

    protected virtual void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
    {
    }

    /// <summary>No-op in this project: there is one DPI-independent classic theme, so there is nothing to rescale.</summary>
    protected static void ScaleArrowOffsetsIfNeeded()
    {
    }

    protected static void ScaleArrowOffsetsIfNeeded(int dpi)
    {
    }
}
