using System.ComponentModel;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace System.Windows.Forms.Layout;

/// <summary>Converts table dimensions, styles and named child cells to the XML used by WinForms resources.</summary>
public class TableLayoutSettingsTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string xml) return base.ConvertFrom(context, culture, value);

        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name.LocalName != "TableLayoutSettings")
            throw new FormatException("Expected a TableLayoutSettings XML element.");

        var settings = new TableLayoutPanel().LayoutSettings;
        settings.ColumnCount = ReadInt(root, "ColumnCount", 0);
        settings.RowCount = ReadInt(root, "RowCount", 0);
        if (root.Attribute("GrowStyle") is { } growStyle)
            settings.GrowStyle = Enum.Parse<TableLayoutPanelGrowStyle>(growStyle.Value);

        ParseStyles(root.Element("Columns"), isColumn: true, settings);
        ParseStyles(root.Element("Rows"), isColumn: false, settings);

        foreach (var control in root.Descendants("Control"))
        {
            var name = (string?)control.Attribute("Name");
            if (string.IsNullOrEmpty(name)) continue;
            settings.RememberCell(name, new TableLayoutSettings.CellSettings(
                ReadInt(control, "Column", -1), ReadInt(control, "Row", -1),
                ReadInt(control, "ColumnSpan", 1), ReadInt(control, "RowSpan", 1)));
        }

        return settings;
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType != typeof(string) || value is not TableLayoutSettings settings)
            return base.ConvertTo(context, culture, value, destinationType);

        var root = new XElement("TableLayoutSettings",
            new XAttribute("ColumnCount", settings.ColumnCount),
            new XAttribute("RowCount", settings.RowCount),
            new XAttribute("GrowStyle", settings.GrowStyle));

        foreach (var cell in settings.NamedCells())
            root.Add(new XElement("Control", new XAttribute("Name", cell.Key),
                new XAttribute("Column", cell.Value.Column), new XAttribute("Row", cell.Value.Row),
                new XAttribute("ColumnSpan", cell.Value.ColumnSpan), new XAttribute("RowSpan", cell.Value.RowSpan)));

        root.Add(new XElement("Columns", new XAttribute("Styles", string.Join(",",
            settings.ColumnStyles.Cast<ColumnStyle>().SelectMany(style => new[]
            {
                style.SizeType.ToString(), style.Width.ToString(CultureInfo.InvariantCulture),
            })))));
        root.Add(new XElement("Rows", new XAttribute("Styles", string.Join(",",
            settings.RowStyles.Cast<RowStyle>().SelectMany(style => new[]
            {
                style.SizeType.ToString(), style.Height.ToString(CultureInfo.InvariantCulture),
            })))));

        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static int ReadInt(XElement element, string attribute, int fallback) =>
        element.Attribute(attribute) is { } value
            ? int.Parse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : fallback;

    private static void ParseStyles(XElement? element, bool isColumn, TableLayoutSettings settings)
    {
        var styles = ((string?)element?.Attribute("Styles"))?.Split(',');
        if (styles is null || styles.Length == 0 || styles[0].Length == 0) return;
        if (styles.Length % 2 != 0) throw new FormatException("Table layout styles require type and size pairs.");

        for (var i = 0; i < styles.Length; i += 2)
        {
            var type = Enum.Parse<SizeType>(styles[i]);
            var size = float.Parse(styles[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (isColumn) settings.ColumnStyles.Add(new ColumnStyle(type, size));
            else settings.RowStyles.Add(new RowStyle(type, size));
        }
    }
}
