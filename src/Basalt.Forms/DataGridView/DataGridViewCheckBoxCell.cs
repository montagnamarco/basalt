using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell whose value is a tri-state check mark: click or Space toggles it immediately (there is
/// no separate edit mode, matching IDataGridViewEditingCell's immediate-commit contract), and it
/// paints with ControlPaint.DrawCheckBox/DrawMixedCheckBox rather than text.
/// </summary>
public partial class DataGridViewCheckBoxCell : DataGridViewCell, IDataGridViewEditingCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    /// <summary>The glyph ControlPaint draws is always 13x13; centred in the cell like Microsoft's own.</summary>
    private const int GlyphSize = 13;

    public DataGridViewCheckBoxCell()
    {
    }

    public DataGridViewCheckBoxCell(bool threeState)
    {
        ThreeState = threeState;
    }

    public override Type? EditType => null;

    internal override bool UsesTextEditorInternal => false;

    public virtual object? EditingCellFormattedValue
    {
        get => GetEditingCellFormattedValue(DataGridViewDataErrorContexts.Formatting);
        set => Value = ParseFormattedValue(value, InheritedStyle, null, null);
    }

    public virtual bool EditingCellValueChanged { get; set; }

    [DefaultValue(null)]
    public object? FalseValue { get; set; }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle { get; set; } = FlatStyle.Standard;

    public override Type FormattedValueType => typeof(CheckState);

    [DefaultValue(null)]
    public object? IndeterminateValue { get; set; }

    [DefaultValue(false)]
    public bool ThreeState { get; set; }

    [DefaultValue(null)]
    public object? TrueValue { get; set; }

    public override Type? ValueType { get; set; } = typeof(bool);

    public override object Clone()
    {
        var clone = (DataGridViewCheckBoxCell)base.Clone();
        clone.ThreeState = ThreeState;
        clone.FalseValue = FalseValue;
        clone.TrueValue = TrueValue;
        clone.IndeterminateValue = IndeterminateValue;
        clone.FlatStyle = FlatStyle;

        return clone;
    }

    protected override bool ContentClickUnsharesRow(DataGridViewCellEventArgs e) => true;

    protected override bool ContentDoubleClickUnsharesRow(DataGridViewCellEventArgs e) => true;

    protected override object? GetFormattedValue(
        object? value,
        int rowIndex,
        ref DataGridViewCellStyle cellStyle,
        TypeConverter? valueTypeConverter,
        TypeConverter? formattedValueTypeConverter,
        DataGridViewDataErrorContexts context) => ToCheckState(value);

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex)
    {
        if (DataGridView is null || ColumnIndex < 0 || rowIndex < 0) return Rectangle.Empty;

        return GetGlyphBounds(new Rectangle(Point.Empty, GetSize(rowIndex)));
    }

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) =>
        new(GlyphSize + cellStyle.Padding.Horizontal + 4, GlyphSize + cellStyle.Padding.Vertical + 4);

    public virtual object GetEditingCellFormattedValue(DataGridViewDataErrorContexts context) => ToCheckState(Value);

    /// <summary>A click on the glyph itself (not merely anywhere in the cell) toggles - the grid raises this only inside GetContentBounds.</summary>
    protected override void OnContentClick(DataGridViewCellEventArgs e)
    {
        _lastContentClickArgs = e;
        Toggle();
    }

    /// <summary>Space toggles the current cell, on key-up as Microsoft's own check box column does (not on key-down, which would fire twice while the key auto-repeats).</summary>
    protected override void OnKeyUp(KeyEventArgs e, int rowIndex)
    {
        if (e.KeyCode != Keys.Space) return;

        SetPressed(false);
        Toggle();
        e.Handled = true;
    }

    /// <summary>Unchecked to checked to (when ThreeState) indeterminate, back to unchecked; read-only cells and rows never toggle.</summary>
    private void Toggle()
    {
        if (ReadOnly || (DataGridView?.ReadOnly ?? false)) return;

        var next = ToCheckState(Value) switch
        {
            CheckState.Unchecked => CheckState.Checked,
            CheckState.Checked => ThreeState ? CheckState.Indeterminate : CheckState.Unchecked,
            _ => CheckState.Unchecked,
        };

        Value = FromCheckState(next);
        DataGridView?.InvalidateCellCore(this);
    }

    public virtual void PrepareEditingCellForEdit(bool selectAll)
    {
    }

    public override object? ParseFormattedValue(object? formattedValue, DataGridViewCellStyle cellStyle, TypeConverter? formattedValueTypeConverter, TypeConverter? valueTypeConverter) =>
        FromCheckState(formattedValue is CheckState state ? state : ToCheckState(formattedValue));

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
        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, null, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);

        if ((paintParts & DataGridViewPaintParts.ContentForeground) == 0) return;

        var glyphBounds = new Rectangle(
            cellBounds.X + (cellBounds.Width - GlyphSize) / 2,
            cellBounds.Y + (cellBounds.Height - GlyphSize) / 2,
            GlyphSize,
            GlyphSize);

        var state = ToCheckState(value);
        var buttonState = (ReadOnly || (DataGridView?.ReadOnly ?? false)) ? ButtonState.Inactive : ButtonState.Normal;

        switch (state)
        {
            case CheckState.Checked:
                ControlPaint.DrawCheckBox(graphics, glyphBounds, buttonState | ButtonState.Checked);
                break;

            case CheckState.Indeterminate:
                ControlPaint.DrawMixedCheckBox(graphics, glyphBounds, buttonState | ButtonState.Checked);
                break;

            default:
                ControlPaint.DrawCheckBox(graphics, glyphBounds, buttonState);
                break;
        }
    }

    public override string ToString() => $"DataGridViewCheckBoxCell {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";

    private static Rectangle GetGlyphBounds(Rectangle cellBounds) => new(
        cellBounds.X + (cellBounds.Width - GlyphSize) / 2,
        cellBounds.Y + (cellBounds.Height - GlyphSize) / 2,
        GlyphSize, GlyphSize);

    /// <summary>Reads the raw Value against TrueValue/FalseValue/IndeterminateValue when set, else treats it as a plain bool/CheckState/null.</summary>
    private CheckState ToCheckState(object? value)
    {
        if (TrueValue is not null && Equals(value, TrueValue)) return CheckState.Checked;
        if (FalseValue is not null && Equals(value, FalseValue)) return CheckState.Unchecked;
        if (IndeterminateValue is not null && Equals(value, IndeterminateValue)) return CheckState.Indeterminate;

        return value switch
        {
            null => CheckState.Unchecked,
            CheckState checkState => checkState,
            bool flag => flag ? CheckState.Checked : CheckState.Unchecked,
            _ => Convert.ToBoolean(value) ? CheckState.Checked : CheckState.Unchecked,
        };
    }

    /// <summary>The inverse of ToCheckState: TrueValue/FalseValue/IndeterminateValue when set, else a plain bool (null for indeterminate without a custom value).</summary>
    private object? FromCheckState(CheckState state) => state switch
    {
        CheckState.Checked => TrueValue ?? true,
        CheckState.Indeterminate => IndeterminateValue,
        _ => FalseValue ?? false,
    };
}
