using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A column of combo-box cells; every property here just forwards to the cell template, so setting it once on the column applies to every row's cell.</summary>
public class DataGridViewComboBoxColumn : DataGridViewColumn
{
    public DataGridViewComboBoxColumn()
        : base(new DataGridViewComboBoxCell())
    {
    }

    [Browsable(true)]
    [DefaultValue(true)]
    public bool AutoComplete
    {
        get => ComboBoxCellTemplate.AutoComplete;
        set => ComboBoxCellTemplate.AutoComplete = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewComboBoxCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewComboBoxCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    [DefaultValue(null)]
    public object? DataSource
    {
        get => ComboBoxCellTemplate.DataSource;
        set => ComboBoxCellTemplate.DataSource = value;
    }

    [DefaultValue("")]
    public string DisplayMember
    {
        get => ComboBoxCellTemplate.DisplayMember;
        set => ComboBoxCellTemplate.DisplayMember = value;
    }

    [DefaultValue(DataGridViewComboBoxDisplayStyle.DropDownButton)]
    public DataGridViewComboBoxDisplayStyle DisplayStyle
    {
        get => ComboBoxCellTemplate.DisplayStyle;
        set => ComboBoxCellTemplate.DisplayStyle = value;
    }

    [DefaultValue(false)]
    public bool DisplayStyleForCurrentCellOnly
    {
        get => ComboBoxCellTemplate.DisplayStyleForCurrentCellOnly;
        set => ComboBoxCellTemplate.DisplayStyleForCurrentCellOnly = value;
    }

    [DefaultValue(1)]
    public int DropDownWidth
    {
        get => ComboBoxCellTemplate.DropDownWidth;
        set => ComboBoxCellTemplate.DropDownWidth = value;
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => ComboBoxCellTemplate.FlatStyle;
        set => ComboBoxCellTemplate.FlatStyle = value;
    }

    [Browsable(false)]
    public DataGridViewComboBoxCell.ObjectCollection Items => ComboBoxCellTemplate.Items;

    [DefaultValue(8)]
    public int MaxDropDownItems
    {
        get => ComboBoxCellTemplate.MaxDropDownItems;
        set => ComboBoxCellTemplate.MaxDropDownItems = value;
    }

    [DefaultValue(false)]
    public bool Sorted
    {
        get => ComboBoxCellTemplate.Sorted;
        set => ComboBoxCellTemplate.Sorted = value;
    }

    [DefaultValue("")]
    public string ValueMember
    {
        get => ComboBoxCellTemplate.ValueMember;
        set => ComboBoxCellTemplate.ValueMember = value;
    }

    private DataGridViewComboBoxCell ComboBoxCellTemplate =>
        (DataGridViewComboBoxCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override object Clone() => base.Clone();

    public override string ToString() => $"DataGridViewComboBoxColumn {{ Name={Name}, Index={Index} }}";
}
