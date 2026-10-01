using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One cell of the grid: holds a value, resolves its effective style by walking up to its row,
/// column and grid, and paints and formats itself through virtual members a derived cell type
/// (CheckBox, ComboBox, Button...) overrides without touching the grid itself.
/// </summary>
public abstract class DataGridViewCell : DataGridViewElement, ICloneable, IDisposable
{
    private DataGridViewCellStyle? _style;
    private object? _value;
    private string _errorText = string.Empty;
    private string _toolTipText = string.Empty;
    private bool _selected;

    protected DataGridViewCell()
    {
    }

    public int ColumnIndex => OwningColumn?.Index ?? -1;

    /// <summary>Where the formatted value is drawn, in client coordinates; empty while unattached to a grid.</summary>
    public Rectangle ContentBounds => DataGridView is null ? Rectangle.Empty : GetContentBounds(RowIndex);

    public virtual object? DefaultNewRowValue => null;

    public virtual bool Displayed => DataGridView is not null && OwningRow is { Displayed: true } && (OwningColumn?.Visible ?? false);

    public virtual Type? EditType => null;

    public object? EditedFormattedValue => GetEditedFormattedValue(RowIndex, DataGridViewDataErrorContexts.Formatting);

    public Rectangle ErrorIconBounds => DataGridView is null ? Rectangle.Empty : GetErrorIconBounds(null!, InheritedStyle, RowIndex);

    public string ErrorText
    {
        get => GetErrorText(RowIndex);
        set => _errorText = value ?? string.Empty;
    }

    public object? FormattedValue => DataGridView is null ? null : GetFormattedValueCore(DataGridViewDataErrorContexts.Formatting);

    public virtual Type? FormattedValueType => ValueType;

    public virtual bool Frozen => OwningRow is { Frozen: true } || (OwningColumn?.Frozen ?? false);

    public bool HasStyle => _style is not null;

    public DataGridViewElementStates InheritedState => GetInheritedState(RowIndex);

    public DataGridViewCellStyle InheritedStyle => GetInheritedStyle(null, RowIndex, includeColors: true);

    public bool IsInEditMode => DataGridView is not null && DataGridView.IsCurrentCellInEditMode && RowIndex == DataGridView.CurrentCellAddress.Y && ColumnIndex == DataGridView.CurrentCellAddress.X;

    private DataGridViewColumn? _owningColumn;
    private DataGridViewRow? _owningRow;

    public DataGridViewColumn? OwningColumn
    {
        get => _owningColumn;
        internal set
        {
            _owningColumn = value;
            RefreshDataGridViewFromOwners();
        }
    }

    public DataGridViewRow? OwningRow
    {
        get => _owningRow;
        internal set
        {
            _owningRow = value;
            RefreshDataGridViewFromOwners();
        }
    }

    /// <summary>
    /// Keeps this cell's own DataGridView field (from DataGridViewElement) in step with its
    /// owners': a data cell follows its row, a column header cell follows its column directly
    /// since it has no row.
    /// </summary>
    private void RefreshDataGridViewFromOwners() => SetDataGridView(_owningRow?.DataGridView ?? _owningColumn?.DataGridView);

    /// <summary>The size the cell would like, which this round simplifies to its current Size rather than measuring content.</summary>
    public Size PreferredSize => Size;

    public virtual bool ReadOnly { get; set; }

    public virtual bool Resizable => OwningRow is { Resizable: DataGridViewTriState.True } || (OwningColumn?.Resizable == DataGridViewTriState.True);

    public int RowIndex => OwningRow?.Index ?? -1;

    public virtual bool Selected
    {
        get => DataGridView?.IsCellSelected(this) ?? _selected;
        set
        {
            if (value == _selected) return;

            _selected = value;
            DataGridView?.SetCellSelectedCore(this, value);
            DataGridView?.InvalidateCellCore(this);
        }
    }

    public Size Size => DataGridView is null ? new Size(-1, -1) : GetSize(RowIndex);

    public DataGridViewCellStyle Style
    {
        get => _style ??= new DataGridViewCellStyle();
        set => _style = value;
    }

    public object? Tag { get; set; }

    public string ToolTipText
    {
        get => _toolTipText;
        set => _toolTipText = value ?? string.Empty;
    }

    public object? Value
    {
        get => GetValue(RowIndex);
        set => SetValueCore(RowIndex, value);
    }

    public virtual Type? ValueType { get; set; }

    public virtual bool Visible => OwningRow is { Visible: true } && (OwningColumn?.Visible ?? false);

    /// <summary>Starts from the four plain edges and narrows each one that touches a neighbour already drawn with its own border.</summary>
    public virtual DataGridViewAdvancedBorderStyle AdjustCellBorderStyle(
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStyleInput,
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStylePlaceholder,
        bool singleVerticalBorderAdded,
        bool singleHorizontalBorderAdded,
        bool isFirstDisplayedColumn,
        bool isFirstDisplayedRow) => dataGridViewAdvancedBorderStyleInput;

    protected virtual Rectangle BorderWidths(DataGridViewAdvancedBorderStyle advancedBorderStyle)
    {
        var left = advancedBorderStyle.Left == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;
        var right = advancedBorderStyle.Right == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;
        var top = advancedBorderStyle.Top == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;
        var bottom = advancedBorderStyle.Bottom == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;

        return new Rectangle(left, top, right, bottom);
    }

    protected virtual bool ClickUnsharesRow(DataGridViewCellEventArgs e) => false;

    object ICloneable.Clone() => Clone();

    public virtual object Clone()
    {
        var clone = (DataGridViewCell)Activator.CreateInstance(GetType())!;
        clone._value = _value;
        clone._errorText = _errorText;
        clone._toolTipText = _toolTipText;
        clone.ValueType = ValueType;
        if (_style is not null) clone._style = _style.Clone();

        return clone;
    }

    protected virtual bool ContentClickUnsharesRow(DataGridViewCellEventArgs e) => false;

    protected virtual bool ContentDoubleClickUnsharesRow(DataGridViewCellEventArgs e) => false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Called when the editing control is about to be removed, for a cell type that needs to detach its own state from it.</summary>
    public virtual void DetachEditingControl()
    {
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    protected virtual bool DoubleClickUnsharesRow(DataGridViewCellEventArgs e) => false;

    protected virtual bool EnterUnsharesRow(int rowIndex, bool throughMouseClick) => false;

    public Rectangle GetContentBounds(int rowIndex) => DataGridView?.GetCellContentBoundsCore(this, rowIndex) ?? Rectangle.Empty;

    protected virtual Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    public object? GetEditedFormattedValue(int rowIndex, DataGridViewDataErrorContexts context) => GetFormattedValueCore(context);

    protected virtual Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    protected virtual string GetErrorText(int rowIndex) => _errorText;

    /// <summary>
    /// Converts the raw value to what the cell draws: a user handler (CellFormatting) runs first
    /// and can supply the formatted value itself; otherwise the value is converted with the style's
    /// Format/FormatProvider, falling back to ToString, and a null value becomes the style's NullValue.
    /// </summary>
    protected virtual object? GetFormattedValue(
        object? value,
        int rowIndex,
        ref DataGridViewCellStyle cellStyle,
        TypeConverter? valueTypeConverter,
        TypeConverter? formattedValueTypeConverter,
        DataGridViewDataErrorContexts context)
    {
        if (value is null || Equals(value, cellStyle.DataSourceNullValue)) return cellStyle.NullValue;
        if (value is string && cellStyle.Format.Length == 0) return value;

        if (value is IFormattable formattable && cellStyle.Format.Length > 0)
        {
            return formattable.ToString(cellStyle.Format, cellStyle.FormatProvider);
        }

        return Convert.ToString(value, cellStyle.FormatProvider);
    }

    public virtual DataGridViewElementStates GetInheritedState(int rowIndex)
    {
        var state = DataGridViewElementStates.None;
        if (Selected) state |= DataGridViewElementStates.Selected;
        if (Visible) state |= DataGridViewElementStates.Visible;
        if (Displayed) state |= DataGridViewElementStates.Displayed;
        if (ReadOnly || (OwningRow?.ReadOnly ?? false) || (OwningColumn?.ReadOnly ?? false) || (DataGridView?.ReadOnly ?? false)) state |= DataGridViewElementStates.ReadOnly;
        if (Frozen) state |= DataGridViewElementStates.Frozen;

        return state;
    }

    /// <summary>
    /// Layers in increasing precedence: the grid's DefaultCellStyle, then the column's, then the
    /// grid's RowsDefaultCellStyle, then the grid's AlternatingRowsDefaultCellStyle (odd rows
    /// only), then the row's own style, then this cell's own Style - matching Microsoft's
    /// documented precedence order exactly.
    /// </summary>
    public virtual DataGridViewCellStyle GetInheritedStyle(DataGridViewCellStyle? inheritedCellStyle, int rowIndex, bool includeColors)
    {
        var resolved = inheritedCellStyle ?? new DataGridViewCellStyle();
        var grid = DataGridView;

        if (grid is not null) resolved.ApplyStyle(grid.DefaultCellStyle);
        if (OwningColumn is { } column && column.HasDefaultCellStyle) resolved.ApplyStyle(column.DefaultCellStyle);

        if (grid is not null)
        {
            resolved.ApplyStyle(grid.RowsDefaultCellStyle);
            if (rowIndex >= 0 && rowIndex % 2 == 1 && grid.HasAlternatingRowsDefaultCellStyle) resolved.ApplyStyle(grid.AlternatingRowsDefaultCellStyle);
        }

        if (OwningRow is { } row && row.HasDefaultCellStyle) resolved.ApplyStyle(row.DefaultCellStyle);
        if (HasStyle) resolved.ApplyStyle(Style);

        if (resolved.Font is null && grid is not null) resolved.Font = grid.Font;
        if (resolved.BackColor.IsEmpty) resolved.BackColor = grid?.DefaultCellStyle.BackColor ?? SystemColors.Window;
        if (resolved.ForeColor.IsEmpty) resolved.ForeColor = grid?.DefaultCellStyle.ForeColor ?? SystemColors.ControlText;
        if (resolved.SelectionBackColor.IsEmpty) resolved.SelectionBackColor = SystemColors.Highlight;
        if (resolved.SelectionForeColor.IsEmpty) resolved.SelectionForeColor = SystemColors.HighlightText;

        return resolved;
    }

    protected virtual Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize) => new(-1, -1);

    protected virtual Size GetSize(int rowIndex) => new(OwningColumn?.Width ?? 0, OwningRow?.Height ?? 0);

    protected virtual object? GetValue(int rowIndex) => _value;

    public virtual void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
    {
    }

    protected virtual bool KeyDownUnsharesRow(KeyEventArgs e, int rowIndex) => false;

    public virtual bool KeyEntersEditMode(KeyEventArgs e) => false;

    protected virtual bool KeyPressUnsharesRow(KeyPressEventArgs e, int rowIndex) => false;

    protected virtual bool KeyUpUnsharesRow(KeyEventArgs e, int rowIndex) => false;

    protected virtual bool LeaveUnsharesRow(int rowIndex, bool throughMouseClick) => false;

    protected virtual bool MouseClickUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected virtual bool MouseDoubleClickUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected virtual bool MouseDownUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected virtual bool MouseEnterUnsharesRow(int rowIndex) => false;

    protected virtual bool MouseLeaveUnsharesRow(int rowIndex) => false;

    protected virtual bool MouseMoveUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected virtual bool MouseUpUnsharesRow(DataGridViewCellMouseEventArgs e) => false;

    protected virtual void OnClick(DataGridViewCellEventArgs e)
    {
    }

    protected virtual void OnContentClick(DataGridViewCellEventArgs e)
    {
    }

    protected virtual void OnContentDoubleClick(DataGridViewCellEventArgs e)
    {
    }

    protected override void OnDataGridViewChanged()
    {
    }

    protected virtual void OnDoubleClick(DataGridViewCellEventArgs e)
    {
    }

    protected virtual void OnEnter(int rowIndex, bool throughMouseClick)
    {
    }

    protected virtual void OnKeyDown(KeyEventArgs e, int rowIndex)
    {
    }

    protected virtual void OnKeyPress(KeyPressEventArgs e, int rowIndex)
    {
    }

    protected virtual void OnKeyUp(KeyEventArgs e, int rowIndex)
    {
    }

    protected virtual void OnLeave(int rowIndex, bool throughMouseClick)
    {
    }

    protected virtual void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
    }

    protected virtual void OnMouseDoubleClick(DataGridViewCellMouseEventArgs e)
    {
    }

    protected virtual void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
    }

    protected virtual void OnMouseEnter(int rowIndex)
    {
    }

    protected virtual void OnMouseLeave(int rowIndex)
    {
    }

    protected virtual void OnMouseMove(DataGridViewCellMouseEventArgs e)
    {
    }

    protected virtual void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
    }

    /// <summary>The grid's only way into the protected Paint below: keeps Paint itself exactly as Microsoft declares it.</summary>
    internal void PaintCore(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts) =>
        Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, ErrorText.Length == 0 ? null : ErrorText, cellStyle, advancedBorderStyle, paintParts);

    /// <summary>Paints the border, the selection/background, the formatted value and the error icon, each gated by paintParts.</summary>
    protected virtual void Paint(
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
        var selected = (cellState & DataGridViewElementStates.Selected) != 0;

        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            using var back = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(back, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.Border) != 0)
        {
            PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
        }

        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0 && formattedValue is not null)
        {
            var textBounds = BorderInset(cellBounds, advancedBorderStyle);
            textBounds.Inflate(-cellStyle.Padding.Horizontal / 2, -cellStyle.Padding.Vertical / 2);

            var flags = TextAlignmentFlags(cellStyle.Alignment);
            TextRenderer.DrawText(graphics, formattedValue.ToString(), cellStyle.Font ?? DataGridView?.Font, textBounds, selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor, flags);
        }
    }

    protected virtual void PaintBorder(Graphics graphics, Rectangle clipBounds, Rectangle bounds, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle)
    {
        using var pen = new Pen(SystemColors.ControlDark);

        if (advancedBorderStyle.Right != DataGridViewAdvancedCellBorderStyle.None) graphics.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        if (advancedBorderStyle.Bottom != DataGridViewAdvancedCellBorderStyle.None) graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
    }

    private static Rectangle BorderInset(Rectangle bounds, DataGridViewAdvancedBorderStyle style)
    {
        var right = style.Right == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;
        var bottom = style.Bottom == DataGridViewAdvancedCellBorderStyle.None ? 0 : 1;

        return new Rectangle(bounds.X + 2, bounds.Y, Math.Max(bounds.Width - 2 - right, 0), Math.Max(bounds.Height - bottom, 0));
    }

    internal static TextFormatFlags TextAlignmentFlags(DataGridViewContentAlignment alignment)
    {
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        flags |= alignment switch
        {
            DataGridViewContentAlignment.TopLeft or DataGridViewContentAlignment.MiddleLeft or DataGridViewContentAlignment.BottomLeft => TextFormatFlags.Left,
            DataGridViewContentAlignment.TopCenter or DataGridViewContentAlignment.MiddleCenter or DataGridViewContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            DataGridViewContentAlignment.TopRight or DataGridViewContentAlignment.MiddleRight or DataGridViewContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };

        flags |= alignment switch
        {
            DataGridViewContentAlignment.TopLeft or DataGridViewContentAlignment.TopCenter or DataGridViewContentAlignment.TopRight => TextFormatFlags.Top,
            DataGridViewContentAlignment.BottomLeft or DataGridViewContentAlignment.BottomCenter or DataGridViewContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter,
        };

        return flags;
    }

    protected virtual void PaintErrorIcon(Graphics graphics, Rectangle clipBounds, Rectangle cellValueBounds, string? errorText)
    {
    }

    /// <summary>Converts a formatted (edited) value back to the type Value holds; the default simply trusts the editor's own type.</summary>
    public virtual object? ParseFormattedValue(object? formattedValue, DataGridViewCellStyle cellStyle, TypeConverter? formattedValueTypeConverter, TypeConverter? valueTypeConverter) => formattedValue;

    public virtual void PositionEditingControl(
        bool setLocation,
        bool setSize,
        Rectangle cellBounds,
        Rectangle cellClip,
        DataGridViewCellStyle cellStyle,
        bool singleVerticalBorderAdded,
        bool singleHorizontalBorderAdded,
        bool isFirstDisplayedColumn,
        bool isFirstDisplayedRow)
    {
        if (DataGridView?.EditingControl is not { } control) return;

        if (setLocation) control.Location = cellBounds.Location;
        if (setSize) control.Size = cellBounds.Size;
    }

    public virtual Rectangle PositionEditingPanel(
        Rectangle cellBounds,
        Rectangle cellClip,
        DataGridViewCellStyle cellStyle,
        bool singleVerticalBorderAdded,
        bool singleHorizontalBorderAdded,
        bool isFirstDisplayedColumn,
        bool isFirstDisplayedRow) => cellBounds;

    protected virtual bool SetValue(int rowIndex, object? value)
    {
        var old = _value;
        _value = value;

        return !Equals(old, value);
    }

    public override string ToString() => $"{GetType().Name} {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";

    /// <summary>Sets the value and, when it really changed, raises CellValueChanged on the owning grid.</summary>
    internal void SetValueCore(int rowIndex, object? value)
    {
        if (SetValue(rowIndex, value)) RaiseCellValueChanged(new DataGridViewCellEventArgs(ColumnIndex, rowIndex));
    }

    private object? GetFormattedValueCore(DataGridViewDataErrorContexts context)
    {
        var cellStyle = InheritedStyle;
        var rowIndex = RowIndex;
        var value = GetValue(rowIndex);

        return DataGridView?.GetFormattedValueCore(this, value, rowIndex, cellStyle, context)
            ?? GetFormattedValue(value, rowIndex, ref cellStyle, null, null, context);
    }
}
