using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public abstract partial class DataGridViewCell
{
    private const TextFormatFlags SupportedMeasurementFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
        TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.WordBreak |
        TextFormatFlags.ExpandTabs | TextFormatFlags.NoClipping | TextFormatFlags.ExternalLeading |
        TextFormatFlags.Internal | TextFormatFlags.TextBoxControl | TextFormatFlags.PathEllipsis |
        TextFormatFlags.EndEllipsis | TextFormatFlags.ModifyString | TextFormatFlags.RightToLeft |
        TextFormatFlags.WordEllipsis | TextFormatFlags.NoFullWidthCharacterBreak |
        TextFormatFlags.HidePrefix | TextFormatFlags.NoPrefix | TextFormatFlags.PrefixOnly |
        TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform |
        TextFormatFlags.NoPadding | TextFormatFlags.LeftAndRightPadding;

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static int MeasureTextHeight(Graphics graphics, string text, Font font, int maxWidth, TextFormatFlags flags) =>
        MeasureTextHeight(graphics, text, font, maxWidth, flags, out _);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static int MeasureTextHeight(Graphics graphics, string text, Font font, int maxWidth, TextFormatFlags flags, out bool widthTruncated)
    {
        ValidateMeasurement(graphics, font, flags);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);

        var naturalSize = MeasureTextSize(graphics, text, font, flags);
        widthTruncated = naturalSize.Width > maxWidth;
        return TextRenderer.MeasureText(graphics, text, font, new Size(maxWidth, int.MaxValue), flags).Height;
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static Size MeasureTextPreferredSize(Graphics graphics, string text, Font font, float maxRatio, TextFormatFlags flags)
    {
        ValidateMeasurement(graphics, font, flags);
        if (float.IsNaN(maxRatio) || maxRatio <= 0) throw new ArgumentOutOfRangeException(nameof(maxRatio));
        if (string.IsNullOrEmpty(text)) return Size.Empty;

        var naturalSize = MeasureTextSize(graphics, text, font, flags);
        if (naturalSize.Height == 0 || naturalSize.Width <= naturalSize.Height * maxRatio ||
            (flags & TextFormatFlags.SingleLine) != 0 || (flags & TextFormatFlags.WordBreak) == 0)
            return naturalSize;

        // With wrapping, narrowing the proposal trades width for height. Keep the widest
        // proposal whose measured aspect ratio fits, so the returned size remains useful
        // to autosizing without choosing an unnecessarily tall block.
        var low = 1;
        var high = naturalSize.Width;
        var best = TextRenderer.MeasureText(graphics, text, font, new Size(low, int.MaxValue), flags);
        while (low <= high)
        {
            var width = low + (high - low) / 2;
            var measured = TextRenderer.MeasureText(graphics, text, font, new Size(width, int.MaxValue), flags);
            if (measured.Height > 0 && measured.Width <= measured.Height * maxRatio)
            {
                best = measured;
                low = width + 1;
            }
            else
            {
                high = width - 1;
            }
        }

        return best;
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static Size MeasureTextSize(Graphics graphics, string text, Font font, TextFormatFlags flags)
    {
        ValidateMeasurement(graphics, font, flags);
        return TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, int.MaxValue), flags);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static int MeasureTextWidth(Graphics graphics, string text, Font font, int maxHeight, TextFormatFlags flags)
    {
        ValidateMeasurement(graphics, font, flags);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

        var naturalSize = MeasureTextSize(graphics, text, font, flags);
        if (naturalSize.Height >= maxHeight || (flags & TextFormatFlags.SingleLine) != 0 ||
            (flags & TextFormatFlags.WordBreak) == 0)
            return naturalSize.Width;

        // Find the narrowest proposal that still fits the height constraint.
        var low = 1;
        var high = naturalSize.Width;
        var best = naturalSize.Width;
        while (low <= high)
        {
            var width = low + (high - low) / 2;
            var measured = TextRenderer.MeasureText(graphics, text, font, new Size(width, int.MaxValue), flags);
            if (measured.Height <= maxHeight)
            {
                best = measured.Width;
                high = width - 1;
            }
            else
            {
                low = width + 1;
            }
        }

        return best;
    }

    private static void ValidateMeasurement(Graphics graphics, Font font, TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(font);
        if ((flags & ~SupportedMeasurementFlags) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
    }
}
