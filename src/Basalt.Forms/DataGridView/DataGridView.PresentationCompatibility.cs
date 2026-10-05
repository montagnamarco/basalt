using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private Panel? _editingPanel;
    private ToolTip? _cellToolTip;
    private bool _showCellToolTips = true;
    private bool _showEditingIcon = true;
    private bool _enableHeadersVisualStyles = true;

    public Panel EditingPanel => _editingPanel ??= new Panel { Visible = false, TabStop = false };

    [DefaultValue(true)]
    public bool EnableHeadersVisualStyles
    {
        get => _enableHeadersVisualStyles;
        set { if (_enableHeadersVisualStyles == value) return; _enableHeadersVisualStyles = value; Invalidate(); }
    }

    [DefaultValue(true)]
    public bool ShowCellToolTips
    {
        get => _showCellToolTips;
        set
        {
            if (_showCellToolTips == value) return;
            _showCellToolTips = value;
            if (!value) _cellToolTip?.SetToolTip(this, null);
        }
    }

    [DefaultValue(true)]
    public bool ShowEditingIcon
    {
        get => _showEditingIcon;
        set { if (_showEditingIcon == value) return; _showEditingIcon = value; Invalidate(); }
    }

    public Cursor UserSetCursor => Cursor;

    public virtual DataGridViewAdvancedBorderStyle AdjustColumnHeaderBorderStyle(
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStyleInput,
        DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStylePlaceholder,
        bool isFirstDisplayedColumn, bool isLastVisibleColumn)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewAdvancedBorderStyleInput);
        ArgumentNullException.ThrowIfNull(dataGridViewAdvancedBorderStylePlaceholder);
        var result = dataGridViewAdvancedBorderStylePlaceholder;
        result.Top = dataGridViewAdvancedBorderStyleInput.Top;
        result.Bottom = dataGridViewAdvancedBorderStyleInput.Bottom;
        result.Left = isFirstDisplayedColumn ? dataGridViewAdvancedBorderStyleInput.Left : DataGridViewAdvancedCellBorderStyle.None;
        result.Right = dataGridViewAdvancedBorderStyleInput.Right;
        return result;
    }

    private void UpdateCellToolTip(DataGridViewCell? cell)
    {
        if (!ShowCellToolTips) return;
        var text = cell?.ToolTipText;
        if (string.IsNullOrEmpty(text) && _cellToolTip is null) return;
        _cellToolTip ??= new ToolTip();
        _cellToolTip.SetToolTip(this, text);
    }

    public class DataGridViewControlCollection : ControlCollection
    {
        public DataGridViewControlCollection(DataGridView owner) : base(owner) { }
        public override void Clear()
        {
            foreach (Control control in this.Cast<Control>().ToArray()) Remove(control);
        }
        public void CopyTo(Control[] array, int index) => base.CopyTo(array, index);
        public void Insert(int index, Control control)
        {
            ArgumentNullException.ThrowIfNull(control);
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
            Add(control);
            SetChildIndex(control, index);
        }
        public override void Remove(Control? control)
        {
            if (Owner is DataGridView grid && (control == grid._horizontalScrollBar || control == grid._verticalScrollBar || control == grid._editingPanel)) return;
            base.Remove(control);
        }
    }
}
