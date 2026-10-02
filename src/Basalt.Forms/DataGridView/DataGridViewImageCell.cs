using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A cell whose value is an Image (or, with valueIsIcon, an Icon), drawn per ImageLayout instead of as text.</summary>
public class DataGridViewImageCell : DataGridViewCell
{
    public DataGridViewImageCell()
    {
    }

    public DataGridViewImageCell(bool valueIsIcon)
    {
        ValueIsIcon = valueIsIcon;
    }

    public override object? DefaultNewRowValue => ValueIsIcon ? null : new Bitmap(1, 1);

    [DefaultValue("")]
    public string Description { get; set; } = string.Empty;

    public override Type? EditType => null;

    internal override bool UsesTextEditorInternal => false;

    public override Type FormattedValueType => ValueIsIcon ? typeof(Icon) : typeof(Image);

    [DefaultValue(DataGridViewImageCellLayout.NotSet)]
    public DataGridViewImageCellLayout ImageLayout { get; set; } = DataGridViewImageCellLayout.NotSet;

    [DefaultValue(false)]
    public bool ValueIsIcon { get; set; }

    public override Type? ValueType
    {
        get => base.ValueType ?? (ValueIsIcon ? typeof(Icon) : typeof(Image));
        set => base.ValueType = value;
    }

    public override object Clone()
    {
        var clone = (DataGridViewImageCell)base.Clone();
        clone.Description = Description;
        clone.ImageLayout = ImageLayout;
        clone.ValueIsIcon = ValueIsIcon;

        return clone;
    }

    protected override object? GetValue(int rowIndex)
    {
        var value = base.GetValue(rowIndex);
        if (value is not null) return value;

        return OwningColumn switch
        {
            DataGridViewImageColumn { ValuesAreIcons: true, Icon: { } icon } => icon,
            DataGridViewImageColumn { ValuesAreIcons: false, Image: { } image } => image,
            _ => null,
        };
    }

    protected override object? GetFormattedValue(
        object? value,
        int rowIndex,
        ref DataGridViewCellStyle cellStyle,
        TypeConverter? valueTypeConverter,
        TypeConverter? formattedValueTypeConverter,
        DataGridViewDataErrorContexts context) => value;

    protected override void Paint(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            var selected = (cellState & DataGridViewElementStates.Selected) != 0;
            using var back = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(back, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0)
        {
            var image = formattedValue switch
            {
                Icon icon => icon.ToBitmap(),
                Image img => img,
                _ => null,
            };

            if (image is not null) DrawImage(graphics, image, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.Border) != 0) PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
    }

    private void DrawImage(Graphics graphics, Image image, Rectangle cellBounds)
    {
        var layout = ImageLayout == DataGridViewImageCellLayout.NotSet ? DataGridViewImageCellLayout.Normal : ImageLayout;

        switch (layout)
        {
            case DataGridViewImageCellLayout.Stretch:
                graphics.DrawImage(image, cellBounds);
                break;

            case DataGridViewImageCellLayout.Zoom:
            {
                var scale = Math.Min((float)cellBounds.Width / image.Width, (float)cellBounds.Height / image.Height);
                var width = (int)(image.Width * scale);
                var height = (int)(image.Height * scale);
                var bounds = new Rectangle(cellBounds.X + (cellBounds.Width - width) / 2, cellBounds.Y + (cellBounds.Height - height) / 2, width, height);
                graphics.DrawImage(image, bounds);
                break;
            }

            default:
            {
                var bounds = new Rectangle(cellBounds.X + (cellBounds.Width - image.Width) / 2, cellBounds.Y + (cellBounds.Height - image.Height) / 2, image.Width, image.Height);
                graphics.DrawImage(image, bounds);
                break;
            }
        }
    }

    public override string ToString() => $"DataGridViewImageCell {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
}
