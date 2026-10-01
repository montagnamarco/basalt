using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The appearance and formatting a cell draws and converts its value with, resolved from the
/// grid's DefaultCellStyle, its column's, its row's, and finally the cell's own Style, each
/// layer filling in only what the layer below left unset.
/// </summary>
public class DataGridViewCellStyle : ICloneable
{
    private object? _nullValue = string.Empty;
    private object? _dataSourceNullValue = DBNull.Value;
    private IFormatProvider? _formatProvider;

    public DataGridViewCellStyle()
    {
    }

    public DataGridViewCellStyle(DataGridViewCellStyle dataGridViewCellStyle)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCellStyle);

        Alignment = dataGridViewCellStyle.Alignment;
        BackColor = dataGridViewCellStyle.BackColor;
        _dataSourceNullValue = dataGridViewCellStyle._dataSourceNullValue;
        Font = dataGridViewCellStyle.Font;
        ForeColor = dataGridViewCellStyle.ForeColor;
        Format = dataGridViewCellStyle.Format;
        _formatProvider = dataGridViewCellStyle._formatProvider;
        _nullValue = dataGridViewCellStyle._nullValue;
        Padding = dataGridViewCellStyle.Padding;
        SelectionBackColor = dataGridViewCellStyle.SelectionBackColor;
        SelectionForeColor = dataGridViewCellStyle.SelectionForeColor;
        Tag = dataGridViewCellStyle.Tag;
        WrapMode = dataGridViewCellStyle.WrapMode;
    }

    public DataGridViewContentAlignment Alignment { get; set; } = DataGridViewContentAlignment.NotSet;

    public Color BackColor { get; set; }

    public object? DataSourceNullValue
    {
        get => _dataSourceNullValue;
        set => _dataSourceNullValue = value;
    }

    public Font? Font { get; set; }

    public Color ForeColor { get; set; }

    public string Format { get; set; } = string.Empty;

    public IFormatProvider FormatProvider
    {
        get => _formatProvider ?? Globalization.CultureInfo.CurrentCulture;
        set => _formatProvider = value;
    }

    public bool IsDataSourceNullValueDefault => Equals(_dataSourceNullValue, DBNull.Value);

    public bool IsFormatProviderDefault => _formatProvider is null;

    public bool IsNullValueDefault => _nullValue is string text && text.Length == 0;

    public object? NullValue
    {
        get => _nullValue;
        set => _nullValue = value;
    }

    public Padding Padding { get; set; }

    public Color SelectionBackColor { get; set; }

    public Color SelectionForeColor { get; set; }

    public object? Tag { get; set; }

    public DataGridViewTriState WrapMode { get; set; } = DataGridViewTriState.NotSet;

    /// <summary>Copies every non-default value of this style onto dataGridViewCellStyle, leaving its own defaults untouched.</summary>
    public virtual void ApplyStyle(DataGridViewCellStyle dataGridViewCellStyle)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCellStyle);

        if (dataGridViewCellStyle.Alignment != DataGridViewContentAlignment.NotSet) Alignment = dataGridViewCellStyle.Alignment;
        if (!dataGridViewCellStyle.BackColor.IsEmpty) BackColor = dataGridViewCellStyle.BackColor;
        if (!dataGridViewCellStyle.IsDataSourceNullValueDefault) _dataSourceNullValue = dataGridViewCellStyle._dataSourceNullValue;
        if (dataGridViewCellStyle.Font is not null) Font = dataGridViewCellStyle.Font;
        if (!dataGridViewCellStyle.ForeColor.IsEmpty) ForeColor = dataGridViewCellStyle.ForeColor;
        if (dataGridViewCellStyle.Format.Length > 0) Format = dataGridViewCellStyle.Format;
        if (!dataGridViewCellStyle.IsFormatProviderDefault) _formatProvider = dataGridViewCellStyle._formatProvider;
        if (!dataGridViewCellStyle.IsNullValueDefault) _nullValue = dataGridViewCellStyle._nullValue;
        if (dataGridViewCellStyle.Padding != Padding.Empty) Padding = dataGridViewCellStyle.Padding;
        if (!dataGridViewCellStyle.SelectionBackColor.IsEmpty) SelectionBackColor = dataGridViewCellStyle.SelectionBackColor;
        if (!dataGridViewCellStyle.SelectionForeColor.IsEmpty) SelectionForeColor = dataGridViewCellStyle.SelectionForeColor;
        if (dataGridViewCellStyle.Tag is not null) Tag = dataGridViewCellStyle.Tag;
        if (dataGridViewCellStyle.WrapMode != DataGridViewTriState.NotSet) WrapMode = dataGridViewCellStyle.WrapMode;
    }

    object ICloneable.Clone() => Clone();

    public virtual DataGridViewCellStyle Clone() => new(this);

    public override bool Equals(object? o) =>
        o is DataGridViewCellStyle other &&
        Alignment == other.Alignment &&
        BackColor == other.BackColor &&
        ForeColor == other.ForeColor &&
        Format == other.Format &&
        Equals(_nullValue, other._nullValue) &&
        Equals(_dataSourceNullValue, other._dataSourceNullValue) &&
        Padding == other.Padding &&
        SelectionBackColor == other.SelectionBackColor &&
        SelectionForeColor == other.SelectionForeColor &&
        WrapMode == other.WrapMode &&
        Equals(Font, other.Font);

    public override int GetHashCode() => HashCode.Combine(Alignment, BackColor, ForeColor, Format, WrapMode);

    public override string ToString() =>
        $"DataGridViewCellStyle {{ BackColor={BackColor}, Font={Font}, ForeColor={ForeColor}, Format={Format}, " +
        $"Alignment={Alignment}, WrapMode={WrapMode} }}";
}
