using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Primitives;

public class PaddingTests
{
    [Fact]
    public void All_is_minus_one_once_a_side_is_set_on_its_own()
    {
        var padding = new Padding(4);
        Assert.Equal(4, padding.All);

        // Setting one side, even to the value it has, ends "all".
        padding.Left = 4;
        Assert.Equal(-1, padding.All);
        Assert.Equal(new Padding(4), padding);

        padding.All = 2;
        Assert.Equal(2, padding.All);
        Assert.Equal(2, padding.Bottom);
    }

    [Fact]
    public void Four_equal_sides_given_to_the_constructor_count_as_all()
    {
        Assert.Equal(3, new Padding(3, 3, 3, 3).All);
        Assert.Equal(-1, new Padding(1, 2, 3, 4).All);
    }

    [Fact]
    public void Sizes_and_arithmetic_work_side_by_side()
    {
        var padding = new Padding(1, 2, 3, 4);

        Assert.Equal(4, padding.Horizontal);
        Assert.Equal(6, padding.Vertical);
        Assert.Equal(new System.Drawing.Size(4, 6), padding.Size);
        Assert.Equal(new Padding(2, 4, 6, 8), padding + padding);
        Assert.Equal(Padding.Empty, padding - padding);
        Assert.Equal("{Left=1,Top=2,Right=3,Bottom=4}", padding.ToString());
    }

    [Fact]
    public void The_converter_reads_and_writes_the_list_with_the_cultures_separator()
    {
        var converter = TypeDescriptor.GetConverter(typeof(Padding));
        var italian = CultureInfo.GetCultureInfo("it-IT");

        Assert.IsType<PaddingConverter>(converter);
        Assert.Equal(new Padding(1, 2, 3, 4), converter.ConvertFrom(null, CultureInfo.InvariantCulture, "1, 2, 3, 4"));
        Assert.Equal("1; 2; 3; 4", converter.ConvertTo(null, italian, new Padding(1, 2, 3, 4), typeof(string)));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "1, 2"));
    }

    [Fact]
    public void The_designer_writes_the_short_constructor_for_equal_sides()
    {
        var converter = new PaddingConverter();

        var all = (InstanceDescriptor)converter.ConvertTo(new Padding(5), typeof(InstanceDescriptor))!;
        var sides = (InstanceDescriptor)converter.ConvertTo(new Padding(1, 2, 3, 4), typeof(InstanceDescriptor))!;

        Assert.Equal([5], all.Arguments.Cast<object>());
        Assert.Equal([1, 2, 3, 4], sides.Arguments.Cast<object>());
    }

    [Fact]
    public void A_property_grid_edit_of_All_sets_every_side()
    {
        var converter = new PaddingConverter();
        var values = new Dictionary<string, object> { ["All"] = 7, ["Left"] = 1, ["Top"] = 2, ["Right"] = 3, ["Bottom"] = 4 };

        // Without the previous value to compare, the sides win.
        Assert.Equal(new Padding(1, 2, 3, 4), converter.CreateInstance(null, values));
    }
}
