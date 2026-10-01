namespace System.Windows.Forms;

/// <summary>A plain text column: its cell template is a DataGridViewTextBoxCell, sortable by default.</summary>
public class DataGridViewTextBoxColumn : DataGridViewColumn
{
    public DataGridViewTextBoxColumn()
        : base(new DataGridViewTextBoxCell())
    {
        SortMode = DataGridViewColumnSortMode.Automatic;
    }

    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewTextBoxCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewTextBoxCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    public int MaxInputLength
    {
        get => TextBoxCellTemplate.MaxInputLength;
        set => TextBoxCellTemplate.MaxInputLength = value;
    }

    public new DataGridViewColumnSortMode SortMode
    {
        get => base.SortMode;
        set => base.SortMode = value;
    }

    private DataGridViewTextBoxCell TextBoxCellTemplate =>
        (DataGridViewTextBoxCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override string ToString() => $"DataGridViewTextBoxColumn {{ Name={Name}, Index={Index} }}";
}
