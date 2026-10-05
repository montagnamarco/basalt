using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewTextMeasurementCompatibilityTests
{
    [Fact]
    public void Height_reports_wrapping_and_whether_natural_width_is_truncated()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        var font = Control.DefaultFont;
        const string text = "one two three four five six seven eight nine ten";
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        var natural = DataGridViewCell.MeasureTextSize(graphics, text, font, flags);

        var height = DataGridViewCell.MeasureTextHeight(graphics, text, font, Math.Max(1, natural.Width / 3), flags, out var truncated);

        Assert.True(truncated);
        Assert.True(height > natural.Height);
        Assert.Equal(height, DataGridViewCell.MeasureTextHeight(graphics, text, font, Math.Max(1, natural.Width / 3), flags));
        Assert.False(DataGridViewCell.MeasureTextHeight(graphics, text, font, natural.Width, flags, out truncated) > natural.Height);
        Assert.False(truncated);
    }

    [Fact]
    public void Preferred_size_respects_aspect_ratio_and_width_respects_height()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        var font = Control.DefaultFont;
        const string text = "one two three four five six seven eight nine ten";
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        var natural = DataGridViewCell.MeasureTextSize(graphics, text, font, flags);

        var preferred = DataGridViewCell.MeasureTextPreferredSize(graphics, text, font, 2f, flags);
        var width = DataGridViewCell.MeasureTextWidth(graphics, text, font, natural.Height * 3, flags);

        Assert.True(preferred.Width <= preferred.Height * 2);
        Assert.True(preferred.Height >= natural.Height);
        Assert.InRange(width, 1, natural.Width);
        Assert.Equal(Size.Empty, DataGridViewCell.MeasureTextPreferredSize(graphics, string.Empty, font, 2f, flags));
    }

    [Fact]
    public void Measurement_rejects_invalid_constraints_and_flags()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        var font = Control.DefaultFont;

        Assert.Throws<ArgumentOutOfRangeException>(() => DataGridViewCell.MeasureTextHeight(graphics, "x", font, 0, TextFormatFlags.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataGridViewCell.MeasureTextWidth(graphics, "x", font, 0, TextFormatFlags.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataGridViewCell.MeasureTextPreferredSize(graphics, "x", font, 0f, TextFormatFlags.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataGridViewCell.MeasureTextSize(graphics, "x", font, (TextFormatFlags)int.MinValue));
    }
}
