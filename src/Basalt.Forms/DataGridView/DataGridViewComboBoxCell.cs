using System.Collections;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell whose value is picked from a list: Items holds the choices directly, or DataSource
/// (with DisplayMember/ValueMember picking which field of each row is shown and which becomes
/// Value) fills Items once from whatever list it is given. DisplayStyle decides whether the cell
/// paints a drop-down button, a full combo-box look, or nothing until it is actually in edit mode.
/// </summary>
public partial class DataGridViewComboBoxCell : DataGridViewCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    /// <summary>The classic drop-down button is always this wide, matching ComboBox's own.</summary>
    private const int ButtonWidth = 16;

    private ObjectCollection? _items;
    private object? _dataSource;
    private bool _current;

    public DataGridViewComboBoxCell()
    {
    }

    [DefaultValue(true)]
    public virtual bool AutoComplete { get; set; } = true;

    public virtual object? DataSource
    {
        get => _dataSource;
        set
        {
            if (value is not null && value is not IList && value is not IListSource)
            {
                throw new ArgumentException("DataSource must be an IList or an IListSource.", nameof(value));
            }

            _dataSource = value;

            // A snapshot, not a live binding: a data source that changes afterwards does not
            // refresh the cell's list (no CurrencyManager is kept for a single cell this round).
            Items.Clear();

            if (ListBindingHelper.GetList(value) is not IList list) return;

            foreach (var item in list)
            {
                if (item is not null) Items.Add(item);
            }
        }
    }

    [DefaultValue("")]
    public virtual string DisplayMember { get; set; } = string.Empty;

    [DefaultValue(DataGridViewComboBoxDisplayStyle.DropDownButton)]
    public DataGridViewComboBoxDisplayStyle DisplayStyle { get; set; } = DataGridViewComboBoxDisplayStyle.DropDownButton;

    [DefaultValue(false)]
    public bool DisplayStyleForCurrentCellOnly { get; set; }

    [DefaultValue(1)]
    public virtual int DropDownWidth { get; set; } = 1;

    public override Type EditType => typeof(DataGridViewComboBoxEditingControl);

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle { get; set; } = FlatStyle.Standard;

    public override Type FormattedValueType => typeof(string);

    [Browsable(false)]
    public virtual ObjectCollection Items => _items ??= new ObjectCollection(this);

    [DefaultValue(8)]
    public virtual int MaxDropDownItems { get; set; } = 8;

    [DefaultValue(false)]
    public virtual bool Sorted { get; set; }

    [DefaultValue("")]
    public virtual string ValueMember { get; set; } = string.Empty;

    public override Type ValueType => base.ValueType ?? typeof(object);

    public override object Clone()
    {
        var clone = (DataGridViewComboBoxCell)base.Clone();
        clone.AutoComplete = AutoComplete;
        clone.DisplayMember = DisplayMember;
        clone.ValueMember = ValueMember;
        clone.DisplayStyle = DisplayStyle;
        clone.DisplayStyleForCurrentCellOnly = DisplayStyleForCurrentCellOnly;
        clone.DropDownWidth = DropDownWidth;
        clone.FlatStyle = FlatStyle;
        clone.MaxDropDownItems = MaxDropDownItems;
        clone.Sorted = Sorted;
        foreach (var item in Items.Snapshot) clone.Items.Add(item);

        return clone;
    }

    public override void DetachEditingControl()
    {
        if (DataGridView?.EditingControl is ComboBox comboBox) comboBox.Visible = false;
    }

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex)
    {
        if (DataGridView is not { } grid || ColumnIndex < 0 || rowIndex < 0) return Rectangle.Empty;

        var bounds = new Rectangle(Point.Empty, GetSize(rowIndex));
        if (DisplayStyle != DataGridViewComboBoxDisplayStyle.Nothing &&
            (!DisplayStyleForCurrentCellOnly || _current)) return bounds;

        return FormattedValue is null ? Rectangle.Empty : GetTextContentBounds(bounds, cellStyle, grid.AdvancedCellBorderStyle);
    }

    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) => GetStandardErrorIconBounds(cellStyle, rowIndex);

    protected override object? GetFormattedValue(
        object? value,
        int rowIndex,
        ref DataGridViewCellStyle cellStyle,
        TypeConverter? valueTypeConverter,
        TypeConverter? formattedValueTypeConverter,
        DataGridViewDataErrorContexts context)
    {
        if (value is null) return cellStyle.NullValue;

        foreach (var item in Items.Snapshot)
        {
            if (Equals(GetItemValue(item), value)) return GetItemDisplayText(item);
        }

        return value.ToString();
    }

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize)
    {
        var text = FormattedValue?.ToString() ?? string.Empty;
        var measured = TextRenderer.MeasureText(text, cellStyle.Font ?? DataGridView?.Font);
        return new Size(measured.Width + ButtonWidth + cellStyle.Padding.Horizontal + 4,
            Math.Max(measured.Height, ButtonWidth) + cellStyle.Padding.Vertical + 2);
    }

    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);

        if (DataGridView?.EditingControl is not ComboBox comboBox) return;

        comboBox.BeginUpdate();
        comboBox.Items.Clear();
        foreach (var item in Items.Snapshot) comboBox.Items.Add(item);
        comboBox.EndUpdate();

        var initialText = initialFormattedValue?.ToString() ?? string.Empty;
        foreach (var item in Items.Snapshot)
        {
            if (!string.Equals(GetItemDisplayText(item), initialText, StringComparison.CurrentCulture)) continue;

            comboBox.SelectedItem = item;
            break;
        }
    }

    /// <summary>Typing a letter or digit opens the drop-down the same way F2 does, matching the text cell's own KeyEntersEditMode.</summary>
    public override bool KeyEntersEditMode(KeyEventArgs e) =>
        !e.Alt && !e.Control &&
        (e.KeyCode is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 || e.KeyCode is >= Keys.NumPad0 and <= Keys.NumPad9);

    protected override void OnDataGridViewChanged()
    {
    }

    protected override void OnEnter(int rowIndex, bool throughMouseClick) => _current = true;

    protected override void OnLeave(int rowIndex, bool throughMouseClick) => _current = false;

    /// <summary>A click anywhere in the cell opens the drop-down, matching the real DataGridViewComboBoxCell (there is no separate hit-test for the button area this round).</summary>
    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
        if (DisplayStyle == DataGridViewComboBoxDisplayStyle.Nothing) return;

        DataGridView?.BeginEdit(true);
        if (DataGridView?.EditingControl is ComboBox comboBox) comboBox.DroppedDown = true;
    }

    protected override void OnMouseEnter(int rowIndex)
    {
    }

    protected override void OnMouseLeave(int rowIndex)
    {
    }

    protected override void OnMouseMove(DataGridViewCellMouseEventArgs e)
    {
    }

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
        var showsButton = DisplayStyle != DataGridViewComboBoxDisplayStyle.Nothing && (!DisplayStyleForCurrentCellOnly || _current);
        var buttonBounds = showsButton
            ? new Rectangle(cellBounds.Right - ButtonWidth, cellBounds.Top, Math.Min(ButtonWidth, cellBounds.Width), cellBounds.Height)
            : Rectangle.Empty;

        base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

        if (showsButton && (paintParts & DataGridViewPaintParts.ContentBackground) != 0)
        {
            var state = (ReadOnly || (DataGridView?.ReadOnly ?? false)) ? ButtonState.Inactive : ButtonState.Normal;
            if (DataGridView?.PaintPortableCellButton(graphics, buttonBounds, cellStyle, state, arrow: true) != true)
            {
                ControlPaint.DrawComboButton(graphics, buttonBounds, state);
            }
        }
    }

    public override object? ParseFormattedValue(object? formattedValue, DataGridViewCellStyle cellStyle, TypeConverter? formattedValueTypeConverter, TypeConverter? valueTypeConverter)
    {
        var text = formattedValue?.ToString() ?? string.Empty;

        foreach (var item in Items.Snapshot)
        {
            if (string.Equals(GetItemDisplayText(item), text, StringComparison.CurrentCulture)) return GetItemValue(item);
        }

        return formattedValue;
    }

    public override string ToString() => $"DataGridViewComboBoxCell {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";

    /// <summary>The text an item shows: the DisplayMember field of it, or the item itself (via ToString) when DisplayMember is empty.</summary>
    internal string GetItemDisplayText(object item) =>
        DisplayMember.Length == 0 ? item.ToString() ?? string.Empty : TypeDescriptor.GetProperties(item).Find(DisplayMember, true)?.GetValue(item)?.ToString() ?? string.Empty;

    /// <summary>What Value becomes once an item is picked: the ValueMember field of it, or the item itself when ValueMember is empty.</summary>
    internal object? GetItemValue(object item) =>
        ValueMember.Length == 0 ? item : TypeDescriptor.GetProperties(item).Find(ValueMember, true)?.GetValue(item);
}
