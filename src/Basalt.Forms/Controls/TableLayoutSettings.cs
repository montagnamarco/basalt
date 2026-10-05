using System.ComponentModel;
using System.Runtime.Serialization;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// A TableLayoutPanel's table seen as settings: column and row counts and
/// styles, growth, and each control's cell. It reads and writes the panel it
/// belongs to; assigning it to another panel copies the table across.
/// </summary>
[TypeConverter(typeof(TableLayoutSettingsTypeConverter))]
public sealed class TableLayoutSettings : LayoutSettings, ISerializable
{
    private readonly TableLayoutPanel _owner;
    private readonly Dictionary<string, CellSettings> _serializedCells = new(StringComparer.Ordinal);

    internal readonly record struct CellSettings(int Column, int Row, int ColumnSpan, int RowSpan);

    internal TableLayoutSettings(TableLayoutPanel owner)
    {
        _owner = owner;
    }

    internal TableLayoutPanel Owner => _owner;

    internal IEnumerable<KeyValuePair<string, CellSettings>> NamedCells()
    {
        foreach (var cell in _serializedCells) yield return cell;

        foreach (Control control in _owner.Controls)
        {
            if (string.IsNullOrEmpty(control.Name) || _serializedCells.ContainsKey(control.Name)) continue;
            yield return new KeyValuePair<string, CellSettings>(control.Name,
                new CellSettings(GetColumn(control), GetRow(control), GetColumnSpan(control), GetRowSpan(control)));
        }
    }

    internal void RememberCell(string name, CellSettings cell) => _serializedCells[name] = cell;

    public override LayoutEngine LayoutEngine => _owner.LayoutEngine;

    [DefaultValue(0)]
    public int ColumnCount
    {
        get => _owner.ColumnCount;
        set => _owner.ColumnCount = value;
    }

    [DefaultValue(0)]
    public int RowCount
    {
        get => _owner.RowCount;
        set => _owner.RowCount = value;
    }

    [DefaultValue(TableLayoutPanelGrowStyle.AddRows)]
    public TableLayoutPanelGrowStyle GrowStyle
    {
        get => _owner.GrowStyle;
        set => _owner.GrowStyle = value;
    }

    public TableLayoutColumnStyleCollection ColumnStyles => _owner.ColumnStyles;

    public TableLayoutRowStyleCollection RowStyles => _owner.RowStyles;

    public int GetColumn(object control) => _owner.GetColumn(AsControl(control));

    public void SetColumn(object control, int column) => _owner.SetColumn(AsControl(control), column);

    public int GetRow(object control) => _owner.GetRow(AsControl(control));

    public void SetRow(object control, int row) => _owner.SetRow(AsControl(control), row);

    public int GetColumnSpan(object control) => _owner.GetColumnSpan(AsControl(control));

    public void SetColumnSpan(object control, int value) => _owner.SetColumnSpan(AsControl(control), value);

    public int GetRowSpan(object control) => _owner.GetRowSpan(AsControl(control));

    public void SetRowSpan(object control, int value) => _owner.SetRowSpan(AsControl(control), value);

    public TableLayoutPanelCellPosition GetCellPosition(object control) => _owner.GetCellPosition(AsControl(control));

    public void SetCellPosition(object control, TableLayoutPanelCellPosition cellPosition) => _owner.SetCellPosition(AsControl(control), cellPosition);

    /// <summary>The settings arrive in .resx files as XML through their converter, not through BinaryFormatter.</summary>
    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) =>
        throw new PlatformNotSupportedException("TableLayoutSettings cannot be serialized with BinaryFormatter on this platform.");

    private static Control AsControl(object control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control as Control ?? throw new NotSupportedException($"TableLayoutSettings only arranges controls, not {control.GetType()}.");
    }

    /// <summary>Copies the table of another panel's settings into the owner's.</summary>
    internal void CopyFrom(TableLayoutSettings source)
    {
        if (source._owner == _owner) return;

        var cells = source.NamedCells().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        _owner.SuspendLayout();

        try
        {
            ColumnCount = source.ColumnCount;
            RowCount = source.RowCount;
            GrowStyle = source.GrowStyle;

            ColumnStyles.Clear();
            foreach (ColumnStyle style in source.ColumnStyles) ColumnStyles.Add(new ColumnStyle(style.SizeType, style.Width));

            RowStyles.Clear();
            foreach (RowStyle style in source.RowStyles) RowStyles.Add(new RowStyle(style.SizeType, style.Height));

            foreach (Control control in _owner.Controls)
            {
                if (string.IsNullOrEmpty(control.Name) || !cells.TryGetValue(control.Name, out var cell)) continue;
                SetColumn(control, cell.Column);
                SetRow(control, cell.Row);
                SetColumnSpan(control, cell.ColumnSpan);
                SetRowSpan(control, cell.RowSpan);
            }
        }
        finally
        {
            _owner.ResumeLayout();
        }
    }
}
