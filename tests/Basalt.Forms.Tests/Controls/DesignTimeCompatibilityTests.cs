using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class DesignTimeCompatibilityTests
{
    [Fact]
    public void Docking_attribute_compares_values_and_recognizes_default()
    {
        Assert.True(new DockingAttribute().IsDefaultAttribute());
        Assert.Equal(DockingBehavior.Never, DockingAttribute.Default.DockingBehavior);
        Assert.Equal(new DockingAttribute(DockingBehavior.Ask), new DockingAttribute(DockingBehavior.Ask));
        Assert.Equal(new DockingAttribute(DockingBehavior.Ask).GetHashCode(), new DockingAttribute(DockingBehavior.Ask).GetHashCode());
        Assert.False(new DockingAttribute(DockingBehavior.AutoDock).IsDefaultAttribute());
    }

    [Fact]
    public void Column_visibility_attribute_defaults_to_visible_and_compares_values()
    {
        Assert.True(new DataGridViewColumnDesignTimeVisibleAttribute().Visible);
        Assert.True(DataGridViewColumnDesignTimeVisibleAttribute.Default.IsDefaultAttribute());
        Assert.True(DataGridViewColumnDesignTimeVisibleAttribute.Yes.IsDefaultAttribute());
        Assert.False(DataGridViewColumnDesignTimeVisibleAttribute.No.IsDefaultAttribute());
        Assert.Equal(DataGridViewColumnDesignTimeVisibleAttribute.No, new DataGridViewColumnDesignTimeVisibleAttribute(false));
    }

    [Theory]
    [InlineData("Ctrl+Shift+A", Keys.Control | Keys.Shift | Keys.A)]
    [InlineData("Control+Alt+Delete", Keys.Control | Keys.Alt | Keys.Delete)]
    [InlineData("F5", Keys.F5)]
    public void Keys_converter_parses_shortcuts(string text, Keys expected)
    {
        var converter = new KeysConverter();

        Assert.Equal(expected, converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
        Assert.Equal(expected, converter.ConvertFrom(null, CultureInfo.InvariantCulture,
            converter.ConvertTo(null, CultureInfo.InvariantCulture, expected, typeof(string))!));
    }

    [Fact]
    public void Keys_converter_rejects_multiple_key_codes_and_exposes_standard_values()
    {
        var converter = new KeysConverter();

        Assert.Throws<FormatException>(() => converter.ConvertFrom("Ctrl+A+B"));
        Assert.Throws<FormatException>(() => converter.ConvertFrom("Ctrl+Unknown"));
        Assert.True(converter.GetStandardValuesSupported());
        Assert.False(converter.GetStandardValuesExclusive());
        Assert.Contains(Keys.F5, converter.GetStandardValues()!.Cast<Keys>());
        Assert.DoesNotContain(Keys.Modifiers, converter.GetStandardValues()!.Cast<Keys>());
        Assert.True(((System.Collections.IComparer)converter).Compare(Keys.A, Keys.B) < 0);
    }

    [Fact]
    public void Link_converter_round_trips_position_and_serializes_link_data()
    {
        var converter = new LinkConverter();
        var link = new LinkLabel.Link(2, 5, "destination");

        var text = (string)converter.ConvertTo(null, CultureInfo.InvariantCulture, link, typeof(string))!;
        var parsed = (LinkLabel.Link)converter.ConvertFrom(null, CultureInfo.InvariantCulture, text)!;
        Assert.Equal((2, 5), (parsed.Start, parsed.Length));

        var descriptor = (InstanceDescriptor)converter.ConvertTo(link, typeof(InstanceDescriptor))!;
        var recreated = (LinkLabel.Link)descriptor.Invoke()!;
        Assert.Equal((2, 5, "destination"), (recreated.Start, recreated.Length, recreated.LinkData));
    }

    [Fact]
    public void List_binding_converter_preserves_source_and_member()
    {
        var converter = new ListBindingConverter();
        var source = new object();
        var binding = new Binding("Text", source, "Customer.Name", true,
            DataSourceUpdateMode.OnPropertyChanged, "missing", "N2", CultureInfo.InvariantCulture);

        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        var descriptor = (InstanceDescriptor)converter.ConvertTo(binding, typeof(InstanceDescriptor))!;
        var recreated = (Binding)descriptor.Invoke()!;
        Assert.Equal("Text", recreated.PropertyName);
        Assert.Same(source, recreated.DataSource);
        Assert.Equal("Customer.Name", recreated.BindingMemberInfo.BindingMember);
        Assert.True(recreated.FormattingEnabled);
        Assert.Equal(DataSourceUpdateMode.OnPropertyChanged, recreated.DataSourceUpdateMode);
        Assert.Equal("missing", recreated.NullValue);
        Assert.Equal("N2", recreated.FormatString);

        var edited = (Binding)converter.CreateInstance(null, new System.Collections.Hashtable
        {
            [nameof(Binding.PropertyName)] = "Value",
            [nameof(Binding.DataSource)] = source,
            ["DataMember"] = "Order.Total",
        });
        Assert.Equal("Order.Total", edited.BindingMemberInfo.BindingMember);
    }
}
