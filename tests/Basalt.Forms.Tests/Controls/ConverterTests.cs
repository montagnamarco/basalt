using System.Globalization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Round-trip coverage for the small property-grid TypeConverters that had none.</summary>
public class ConverterTests
{
    [Theory]
    [InlineData("50", 0.5)]
    [InlineData("50%", 0.5)]
    [InlineData("100", 1.0)]
    [InlineData("0", 0.0)]
    public void Opacity_converter_treats_a_bare_number_as_a_percentage_like_one_with_a_percent_sign(string text, double expected)
    {
        var converter = new OpacityConverter();

        Assert.Equal(expected, (double)converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
    }

    [Fact]
    public void Opacity_converter_round_trips_through_ConvertTo()
    {
        var converter = new OpacityConverter();

        var text = (string)converter.ConvertTo(null, CultureInfo.InvariantCulture, 0.5, typeof(string));
        Assert.Equal("50%", text);
        Assert.Equal(0.5, (double)converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
    }

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.999, "99%")]
    [InlineData(0.375, "37%")]
    public void Opacity_converter_formats_whole_percent_truncating_like_WinForms(double opacity, string expected)
    {
        var converter = new OpacityConverter();

        Assert.Equal(expected, converter.ConvertTo(null, CultureInfo.InvariantCulture, opacity, typeof(string)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Opacity_converter_rejects_a_percentage_out_of_range(int percent)
    {
        var converter = new OpacityConverter();

        Assert.Throws<ArgumentException>(() =>
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, percent.ToString(CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(Keys.Control | Keys.C, "Ctrl+C")]
    [InlineData(Keys.Control | Keys.Alt | Keys.Delete, "Ctrl+Alt+Delete")]
    [InlineData(Keys.F5, "F5")]
    public void Keys_converter_round_trips_a_shortcut_through_its_string_form(Keys keys, string text)
    {
        var converter = new KeysConverter();

        Assert.Equal(text, converter.ConvertTo(null, CultureInfo.InvariantCulture, keys, typeof(string)));
        Assert.Equal(keys, converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
    }

    [Fact]
    public void Keys_converter_rejects_a_shortcut_with_two_key_codes()
    {
        var converter = new KeysConverter();

        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "A+B"));
    }

    [Fact]
    public void Image_index_converter_round_trips_a_plain_index_through_its_string_form()
    {
        var converter = new ImageIndexConverter();

        var text = (string)converter.ConvertTo(null, CultureInfo.InvariantCulture, 2, typeof(string));
        Assert.Equal("2", text);
        Assert.Equal(2, converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
    }

    [Fact]
    public void Image_key_converter_round_trips_a_plain_key_through_its_string_form()
    {
        var converter = new ImageKeyConverter();

        var text = (string)converter.ConvertTo(null, CultureInfo.InvariantCulture, "icon", typeof(string));
        Assert.Equal("icon", text);
        Assert.Equal("icon", converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
    }
}
