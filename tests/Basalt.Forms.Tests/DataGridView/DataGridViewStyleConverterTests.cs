using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewStyleConverterTests
{
    [Fact]
    public void TypeDescriptor_uses_a_constructor_descriptor_that_leaves_properties_for_serialization()
    {
        var style = new DataGridViewCellStyle { BackColor = Color.Red, Format = "N2" };
        var converter = TypeDescriptor.GetConverter(style);

        Assert.IsType<DataGridViewCellStyleConverter>(converter);
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        var descriptor = Assert.IsType<InstanceDescriptor>(converter.ConvertTo(style, typeof(InstanceDescriptor)));
        Assert.False(descriptor.IsComplete);
        Assert.IsType<DataGridViewCellStyle>(descriptor.Invoke());
    }
}
