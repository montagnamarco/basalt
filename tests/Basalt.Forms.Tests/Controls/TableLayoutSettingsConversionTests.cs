using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace Basalt.Forms.Tests.Controls;

public class TableLayoutSettingsConversionTests
{
    [Fact]
    public void Xml_round_trip_restores_styles_and_named_control_cells_on_another_panel()
    {
        using var source = new TableLayoutPanel { ColumnCount = 3, RowCount = 3 };
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        source.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var child = new Button { Name = "action" };
        source.Controls.Add(child);
        source.SetCellPosition(child, new TableLayoutPanelCellPosition(1, 2));
        source.SetColumnSpan(child, 2);

        var converter = TypeDescriptor.GetConverter(source.LayoutSettings);
        Assert.IsType<TableLayoutSettingsTypeConverter>(converter);
        var xml = converter.ConvertToInvariantString(source.LayoutSettings)!;
        var restored = Assert.IsType<TableLayoutSettings>(converter.ConvertFromInvariantString(xml));

        using var target = new TableLayoutPanel();
        var matchingChild = new Button { Name = "action" };
        target.Controls.Add(matchingChild);
        target.LayoutSettings = restored;

        Assert.Equal(3, target.ColumnCount);
        Assert.Equal(3, target.RowCount);
        Assert.Equal(75, target.ColumnStyles[0].Width);
        Assert.Equal(36, target.RowStyles[0].Height);
        Assert.Equal(1, target.GetColumn(matchingChild));
        Assert.Equal(2, target.GetRow(matchingChild));
        Assert.Equal(2, target.GetColumnSpan(matchingChild));
    }

    [Fact]
    public void Xml_converter_rejects_document_type_entities()
    {
        var converter = new TableLayoutSettingsTypeConverter();
        const string xml = "<!DOCTYPE TableLayoutSettings [<!ENTITY x 'bad'>]><TableLayoutSettings />";

        Assert.Throws<System.Xml.XmlException>(() => converter.ConvertFromInvariantString(xml));
    }
}
