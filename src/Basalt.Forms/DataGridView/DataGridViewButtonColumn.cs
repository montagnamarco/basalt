using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A column of push-button cells; Text and UseColumnTextForButtonValue let every cell in the column share one caption instead of each holding its own Value.</summary>
public class DataGridViewButtonColumn : DataGridViewColumn
{
    public DataGridViewButtonColumn()
        : base(new DataGridViewButtonCell())
    {
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewButtonCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewButtonCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set => base.DefaultCellStyle = value;
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => ButtonCellTemplate.FlatStyle;
        set => ButtonCellTemplate.FlatStyle = value;
    }

    [DefaultValue(null)]
    public string? Text { get; set; }

    [DefaultValue(false)]
    public bool UseColumnTextForButtonValue
    {
        get => ButtonCellTemplate.UseColumnTextForButtonValue;
        set => ButtonCellTemplate.UseColumnTextForButtonValue = value;
    }

    private DataGridViewButtonCell ButtonCellTemplate =>
        (DataGridViewButtonCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override object Clone()
    {
        var clone = (DataGridViewButtonColumn)base.Clone();
        clone.Text = Text;

        return clone;
    }

    public override string ToString() => $"DataGridViewButtonColumn {{ Name={Name}, Index={Index} }}";
}
