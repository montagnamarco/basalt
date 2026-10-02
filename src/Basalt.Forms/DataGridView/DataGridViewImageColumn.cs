using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A column of image cells; a column-wide Image or Icon paints in every cell when the row's own Value is null.</summary>
public class DataGridViewImageColumn : DataGridViewColumn
{
    public DataGridViewImageColumn()
        : base(new DataGridViewImageCell())
    {
    }

    public DataGridViewImageColumn(bool valuesAreIcons)
        : base(new DataGridViewImageCell(valuesAreIcons))
    {
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewImageCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewImageCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set => base.DefaultCellStyle = value;
    }

    [DefaultValue("")]
    public string Description
    {
        get => ImageCellTemplate.Description;
        set => ImageCellTemplate.Description = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Icon? Icon { get; set; }

    [DefaultValue(null)]
    public Image? Image { get; set; }

    [DefaultValue(DataGridViewImageCellLayout.Normal)]
    public DataGridViewImageCellLayout ImageLayout
    {
        get => ImageCellTemplate.ImageLayout;
        set => ImageCellTemplate.ImageLayout = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ValuesAreIcons
    {
        get => ImageCellTemplate.ValueIsIcon;
        set => ImageCellTemplate.ValueIsIcon = value;
    }

    private DataGridViewImageCell ImageCellTemplate =>
        (DataGridViewImageCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override object Clone()
    {
        var clone = (DataGridViewImageColumn)base.Clone();
        clone.Icon = Icon;
        clone.Image = Image;

        return clone;
    }

    public override string ToString() => $"DataGridViewImageColumn {{ Name={Name}, Index={Index} }}";
}
