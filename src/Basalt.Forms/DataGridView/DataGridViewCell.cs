using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One cell of the grid: holds a value, resolves its effective style by walking up to its row,
/// column and grid, and paints and formats itself through virtual members a derived cell type
/// (CheckBox, ComboBox, Button...) overrides without touching the grid itself.
/// </summary>
public abstract partial class DataGridViewCell : DataGridViewElement, ICloneable, IDisposable
{
    private DataGridViewCellStyle? _style;
    private object? _value;
    private string _errorText = string.Empty;
    private string _toolTipText = string.Empty;
    private bool _selected;
    private ContextMenuStrip? _contextMenuStrip;

    protected DataGridViewCell()
    {
    }

    public int ColumnIndex => OwningColumn?.Index ?? -1;

    /// <summary>
    /// The cell's own menu, or - while it has none and a grid owns it - whatever
    /// CellContextMenuStripNeeded supplies this time, asked again on every read rather than cached,
    /// exactly as ErrorText asks CellErrorTextNeeded fresh in virtual mode.
    /// </summary>
    [Browsable(false)]
    public virtual ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip ?? (DataGridView is { } grid ? grid.GetCellContextMenuStripCore(this, RowIndex) : null);
        set
        {
            if (ReferenceEquals(_contextMenuStrip, value)) return;

            _contextMenuStrip = value;
            DataGridView?.OnCellContextMenuStripChangedCore(this);
        }
    }

    /// <summary>Where the formatted value is drawn, in cell-local coordinates; empty while unattached to a grid.</summary>
    public Rectangle ContentBounds => DataGridView is null ? Rectangle.Empty : GetContentBounds(RowIndex);

    public virtual object? DefaultNewRowValue => null;

    public virtual bool Displayed => DataGridView is not null && OwningRow is { Displayed: true } && (OwningColumn?.Visible ?? false);

    public virtual Type? EditType => null;

    /// <summary>
    /// Whether typing or F2 opens a text editor over the cell. A check box, button, link or image
    /// cell has no editing control and says no, so a Space meant for the check box does not open one.
    /// </summary>
    internal virtual bool UsesTextEditorInternal => true;

    public object? EditedFormattedValue => GetEditedFormattedValue(RowIndex, DataGridViewDataErrorContexts.Formatting);

    public Rectangle ErrorIconBounds
    {
        get
        {
            if (DataGridView is null) return Rectangle.Empty;
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            return GetErrorIconBounds(graphics, InheritedStyle, RowIndex);
        }
    }

    public string ErrorText
    {
        get => GetErrorText(RowIndex);
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _errorText) return;

            _errorText = newValue;
            DataGridView?.OnCellErrorTextChangedCore(this);
        }
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

    /// <summary>The size requested by the cell's content, or its current size when that cell does not measure content.</summary>
    public Size PreferredSize
    {
        get
        {
            if (DataGridView is null) return Size;

            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            var preferred = GetPreferredSize(graphics, InheritedStyle, RowIndex, Size.Empty);
            return preferred.Width < 0 || preferred.Height < 0 ? Size : preferred;
        }
    }

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
            DataGridView?.OnCellStateChangedCore(this, DataGridViewElementStates.Selected);
            DataGridView?.InvalidateCellCore(this);
        }
    }

    public Size Size => DataGridView is null ? new Size(-1, -1) : GetSize(RowIndex);

    public DataGridViewCellStyle Style
    {
        get => _style ??= new DataGridViewCellStyle();
        set
        {
            _style = value;
            DataGridView?.OnCellStyleChangedCore(this);
            if (value is not null) DataGridView?.OnCellStyleContentChangedCore(value, DataGridViewCellStyleScopes.Cell);
        }
    }

    public object? Tag { get; set; }

    public string ToolTipText
    {
        get => _toolTipText.Length > 0 || DataGridView is not { } grid ? _toolTipText : grid.GetCellToolTipTextCore(this, RowIndex) ?? string.Empty;
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _toolTipText) return;

            _toolTipText = newValue;
            DataGridView?.OnCellToolTipTextChangedCore(this);
        }
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

    ~DataGridViewCell() => Dispose(false);

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

    internal Rectangle GetContentBoundsCore(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        GetContentBounds(graphics, cellStyle, rowIndex);

    public object? GetEditedFormattedValue(int rowIndex, DataGridViewDataErrorContexts context) => GetFormattedValueCore(context);

    protected virtual Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => Rectangle.Empty;

    internal Rectangle GetStandardErrorIconBounds(DataGridViewCellStyle cellStyle, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(cellStyle);
        if (DataGridView is not { } grid || rowIndex < 0 || rowIndex >= grid.Rows.Count
            || string.IsNullOrEmpty(GetErrorText(rowIndex))
            || (ColumnIndex >= 0 ? !grid.ShowCellErrors : !grid.ShowRowErrors)) return Rectangle.Empty;
        var bounds = new Rectangle(Point.Empty, GetSize(rowIndex));
        var borderStyle = ColumnIndex < 0
            ? grid.GetRowHeaderBorderStyleCore(rowIndex)
            : grid.AdvancedCellBorderStyle;
        return GetErrorGlyphBounds(GetTextContentBounds(bounds, cellStyle, borderStyle));
    }

    private Rectangle GetErrorGlyphBounds(Rectangle content)
    {
        const int size = 12;
        const int margin = 3;
        if (content.Width < size + margin * 2 || content.Height < size + margin * 2) return Rectangle.Empty;
        var left = DataGridView?.RightToLeft == RightToLeft.Yes ? content.Left + margin : content.Right - margin - size;
        return new Rectangle(left, content.Top + (content.Height - size) / 2, size, size);
    }

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

        if (grid is not null) resolved.ApplyStyle(grid.GetPortableDefaultCellStyle());
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

    protected virtual object? GetValue(int rowIndex) => DataGridView is { VirtualMode: true } grid ? grid.GetCellValueCore(this, rowIndex) : _value;

    /// <summary>
    /// Walks cell, then row, then column, then the grid's own ContextMenuStrip (Control's), asking
    /// CellContextMenuStripNeeded for the cell step the same way the public ContextMenuStrip getter
    /// does, so a handler supplying one dynamically is honoured here too.
    /// </summary>
    public virtual ContextMenuStrip? GetInheritedContextMenuStrip(int rowIndex)
    {
        if (ContextMenuStrip is { } own) return own;
        if (OwningRow is { } row && row.GetContextMenuStrip(rowIndex) is { } rowStrip) return rowStrip;
        if (OwningColumn?.ContextMenuStrip is { } columnStrip) return columnStrip;

        return DataGridView?.ContextMenuStrip;
    }

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

    /// <summary>
    /// The grid's only way into the protected mouse/keyboard/click virtuals below: called straight
    /// from hit-testing in DataGridView.Input.cs so a cell reacts the moment it is clicked or
    /// keyed, whether or not it has ever been painted, and without the grid holding a lasting
    /// delegate onto the cell the way a subscription added lazily from Paint would.
    /// </summary>
    internal void OnMouseDownCore(DataGridViewCellMouseEventArgs e) => OnMouseDown(e);

    internal void OnMouseUpCore(DataGridViewCellMouseEventArgs e) => OnMouseUp(e);

    internal void OnMouseMoveCore(DataGridViewCellMouseEventArgs e) => OnMouseMove(e);

    internal void OnMouseEnterCore(int rowIndex) => OnMouseEnter(rowIndex);

    internal void OnMouseLeaveCore(int rowIndex) => OnMouseLeave(rowIndex);

    internal void OnClickCore(DataGridViewCellEventArgs e) => OnClick(e);

    internal void OnDoubleClickCore(DataGridViewCellEventArgs e) => OnDoubleClick(e);

    internal void OnContentClickCore(DataGridViewCellEventArgs e) => OnContentClick(e);

    internal void OnContentDoubleClickCore(DataGridViewCellEventArgs e) => OnContentDoubleClick(e);

    internal void OnKeyDownCore(KeyEventArgs e, int rowIndex) => OnKeyDown(e, rowIndex);

    internal void OnKeyUpCore(KeyEventArgs e, int rowIndex) => OnKeyUp(e, rowIndex);

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
        DataGridViewPaintParts paintParts,
        string? errorText = null)
    {
        var resolvedError = errorText ?? (ErrorText.Length == 0 ? null : ErrorText);
        Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue,
            resolvedError, cellStyle, advancedBorderStyle, paintParts);
        // Paint after the concrete cell's content so stretched images cannot hide the error.
        if ((paintParts & DataGridViewPaintParts.ErrorIcon) != 0 && rowIndex >= 0
            && (ColumnIndex >= 0 ? DataGridView?.ShowCellErrors : DataGridView?.ShowRowErrors) == true)
            PaintErrorIcon(graphics, clipBounds, GetTextContentBounds(cellBounds, cellStyle, advancedBorderStyle), resolvedError);
    }

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
            var textBounds = GetTextContentBounds(cellBounds, cellStyle, advancedBorderStyle);

            var flags = TextAlignmentFlags(cellStyle.Alignment);
            TextRenderer.DrawText(graphics, formattedValue.ToString(), cellStyle.Font ?? DataGridView?.Font, textBounds, selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor, flags);
        }
    }

    protected virtual void PaintBorder(Graphics graphics, Rectangle clipBounds, Rectangle bounds, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle)
    {
        using var pen = new Pen(DataGridView?.PortableGridLineColor ?? SystemColors.ControlDark);

        if (advancedBorderStyle.Right != DataGridViewAdvancedCellBorderStyle.None) graphics.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        if (advancedBorderStyle.Bottom != DataGridViewAdvancedCellBorderStyle.None) graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
    }

    internal static Rectangle GetTextContentBounds(Rectangle bounds, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle borderStyle)
    {
        var content = BorderInset(bounds, borderStyle);
        content.Inflate(-cellStyle.Padding.Horizontal / 2, -cellStyle.Padding.Vertical / 2);
        return content;
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
        ArgumentNullException.ThrowIfNull(graphics);
        if (DataGridView is null || string.IsNullOrEmpty(errorText)) return;
        var bounds = GetErrorGlyphBounds(cellValueBounds);
        if (bounds.IsEmpty || !clipBounds.IntersectsWith(bounds)) return;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(clipBounds, System.Drawing.Drawing2D.CombineMode.Intersect);
            graphics.DrawIcon(SystemIcons.Error, bounds);
        }
        finally { graphics.Restore(state); }
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
        if (DataGridView is { VirtualMode: true } grid)
        {
            var old = grid.GetCellValueCore(this, rowIndex);
            grid.SetCellValueCore(this, rowIndex, value);

            return !Equals(old, value);
        }

        var previous = _value;
        _value = value;

        return !Equals(previous, value);
    }

    public override string ToString() => $"{GetType().Name} {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";

    /// <summary>Sets the value and repaints the cell when it changes, including for callers that update it outside an editing control.</summary>
    internal void SetValueCore(int rowIndex, object? value)
    {
        if (!SetValue(rowIndex, value)) return;

        RaiseCellValueChanged(new DataGridViewCellEventArgs(ColumnIndex, rowIndex));
        DataGridView?.InvalidateCellCore(this);
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
