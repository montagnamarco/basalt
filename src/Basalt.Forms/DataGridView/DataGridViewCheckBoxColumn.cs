using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A column whose cells are DataGridViewCheckBoxCell: the TrueValue/FalseValue/IndeterminateValue/ThreeState/FlatStyle properties here only forward to the cell template, as Microsoft's own column does.</summary>
public class DataGridViewCheckBoxColumn : DataGridViewColumn
{
    public DataGridViewCheckBoxColumn()
        : base(new DataGridViewCheckBoxCell())
    {
    }

    public DataGridViewCheckBoxColumn(bool threeState)
        : base(new DataGridViewCheckBoxCell(threeState))
    {
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewCheckBoxCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewCheckBoxCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    public override DataGridViewCellStyle DefaultCellStyle
    {
        get => base.DefaultCellStyle;
        set => base.DefaultCellStyle = value;
    }

    [DefaultValue(null)]
    public object? FalseValue
    {
        get => CheckBoxCellTemplate.FalseValue;
        set => CheckBoxCellTemplate.FalseValue = value;
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => CheckBoxCellTemplate.FlatStyle;
        set => CheckBoxCellTemplate.FlatStyle = value;
    }

    [DefaultValue(null)]
    public object? IndeterminateValue
    {
        get => CheckBoxCellTemplate.IndeterminateValue;
        set => CheckBoxCellTemplate.IndeterminateValue = value;
    }

    [DefaultValue(false)]
    public bool ThreeState
    {
        get => CheckBoxCellTemplate.ThreeState;
        set => CheckBoxCellTemplate.ThreeState = value;
    }

    [DefaultValue(null)]
    public object? TrueValue
    {
        get => CheckBoxCellTemplate.TrueValue;
        set => CheckBoxCellTemplate.TrueValue = value;
    }

    private DataGridViewCheckBoxCell CheckBoxCellTemplate =>
        (DataGridViewCheckBoxCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override string ToString() => $"DataGridViewCheckBoxColumn {{ Name={Name}, Index={Index} }}";
}
