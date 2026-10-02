using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// The drop-down list: a borderless popup window for
/// <see cref="ComboBoxStyle.DropDown"/> and <see cref="ComboBoxStyle.DropDownList"/>,
/// or an area painted directly below the edit, never closed, for
/// <see cref="ComboBoxStyle.Simple"/>.
/// </summary>
/// <remarks>
/// The popup (<see cref="DropDownPopup"/>) only paints and hit-tests; the
/// combo box itself keeps the focus throughout, exactly as a real combo
/// box's list window never takes it. That lets F4, Alt+Down, the arrow keys
/// and Escape/Enter keep working on the control the user is already on.
/// </remarks>
public partial class ComboBox
{
    private DropDownPopup? _dropDownPopup;
    private bool _droppedDown;
    private int _dropDownHighlight = -1;
    private int _dropDownTopIndex;
    private int _preDropDownSelectedIndex = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DroppedDown
    {
        get => _droppedDown;
        set
        {
            if (value) OpenDropDown();
            else CloseDropDown(revert: false);
        }
    }

    public event EventHandler? DropDown
    {
        add => Events.AddHandler(s_dropDownEvent, value);
        remove => Events.RemoveHandler(s_dropDownEvent, value);
    }

    public event EventHandler? DropDownClosed
    {
        add => Events.AddHandler(s_dropDownClosedEvent, value);
        remove => Events.RemoveHandler(s_dropDownClosedEvent, value);
    }

    public event DrawItemEventHandler? DrawItem
    {
        add => Events.AddHandler(s_drawItemEvent, value);
        remove => Events.RemoveHandler(s_drawItemEvent, value);
    }

    public event MeasureItemEventHandler? MeasureItem
    {
        add => Events.AddHandler(s_measureItemEvent, value);
        remove => Events.RemoveHandler(s_measureItemEvent, value);
    }

    private static readonly object s_dropDownEvent = new();
    private static readonly object s_dropDownClosedEvent = new();
    private static readonly object s_drawItemEvent = new();
    private static readonly object s_measureItemEvent = new();

    protected virtual void OnDropDown(EventArgs e) => ((EventHandler?)Events[s_dropDownEvent])?.Invoke(this, e);

    protected virtual void OnDropDownClosed(EventArgs e) => ((EventHandler?)Events[s_dropDownClosedEvent])?.Invoke(this, e);

    protected virtual void OnDrawItem(DrawItemEventArgs e) => ((DrawItemEventHandler?)Events[s_drawItemEvent])?.Invoke(this, e);

    protected virtual void OnMeasureItem(MeasureItemEventArgs e) => ((MeasureItemEventHandler?)Events[s_measureItemEvent])?.Invoke(this, e);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_dropDownStyle != ComboBoxStyle.Simple && ButtonBounds.Contains(e.Location))
        {
            DroppedDown = !_droppedDown;
            return;
        }

        if (_dropDownStyle == ComboBoxStyle.DropDownList)
        {
            DroppedDown = !_droppedDown;
            return;
        }

        if (_dropDownStyle == ComboBoxStyle.Simple)
        {
            var listBounds = Rectangle.FromLTRB(ClientRectangle.Left, EditBounds.Bottom, ClientRectangle.Right, ClientRectangle.Bottom);
            if (!listBounds.Contains(e.Location)) return;

            var index = IndexFromDropDownPoint(new Point(e.Location.X, e.Location.Y - listBounds.Top));
            if (index >= 0) CommitSelection(index);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        if (_droppedDown)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: MoveDropDownHighlight(-1); e.Handled = true; return;
                case Keys.Down: MoveDropDownHighlight(1); e.Handled = true; return;
                case Keys.Home: SetDropDownHighlight(0); e.Handled = true; return;
                case Keys.End: SetDropDownHighlight(_items.Count - 1); e.Handled = true; return;
                case Keys.PageUp: MoveDropDownHighlight(-VisibleDropDownRows()); e.Handled = true; return;
                case Keys.PageDown: MoveDropDownHighlight(VisibleDropDownRows()); e.Handled = true; return;
            }

            return;
        }

        // The AutoComplete suggestion list (ComboBox.Hidden.cs), not this drop-down list: Up/Down move
        // its highlight and Enter accepts it instead of stepping the selection or doing nothing.
        if (_suggestionPopup is { IsOpen: true } popup)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: popup.MoveHighlight(-1); e.Handled = true; return;
                case Keys.Down: popup.MoveHighlight(1); e.Handled = true; return;
                case Keys.Enter when popup.AcceptHighlighted(): e.Handled = true; return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.Up: StepSelection(-1); e.Handled = true; return;
            case Keys.Down: StepSelection(1); e.Handled = true; return;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled) return;
        if (_dropDownStyle != ComboBoxStyle.DropDownList) return;
        if (char.IsControl(e.KeyChar)) return;

        var start = _droppedDown ? _dropDownHighlight : _selectedIndex;
        var index = FindStringCore(e.KeyChar.ToString(), start, exact: false);

        if (index >= 0)
        {
            if (_droppedDown) SetDropDownHighlight(index);
            else CommitSelection(index);
        }

        e.Handled = true;
    }

    /// <summary>
    /// F4, Alt+Down, Escape and Enter are consumed as commands, before the
    /// control's own OnKeyDown and before a dialog could treat Enter or
    /// Escape as its accept or cancel button, exactly as a real combo box's
    /// drop-down swallows them.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_droppedDown)
        {
            if (keyData == Keys.Escape)
            {
                CloseDropDown(revert: true);
                return true;
            }

            if (keyData == Keys.Enter)
            {
                CommitSelection(_dropDownHighlight);
                CloseDropDown(revert: false);
                return true;
            }

            if (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down))
            {
                CloseDropDown(revert: false);
                return true;
            }
        }
        else if (_dropDownStyle != ComboBoxStyle.Simple && (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down)))
        {
            OpenDropDown();
            return true;
        }
        else if (keyData == Keys.Escape && _suggestionPopup is { IsOpen: true })
        {
            HideSuggestions();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        using (var back = new SolidBrush(EffectiveBackColor)) graphics.FillRectangle(back, client);

        PaintEditArea(graphics);

        if (_dropDownStyle != ComboBoxStyle.Simple)
        {
            var buttonState = !Enabled ? ButtonState.Inactive : _droppedDown ? ButtonState.Pushed : ButtonState.Normal;
            ControlPaint.DrawComboButton(graphics, ButtonBounds, buttonState);
        }
        else
        {
            var listBounds = Rectangle.FromLTRB(client.Left, EditBounds.Bottom, client.Right, client.Bottom);
            PaintDropDownItems(graphics, listBounds);
        }
    }

    /// <summary>Commits an item as the user's own action (a click, Enter, or an arrow key), raising SelectionChangeCommitted.</summary>
    private void CommitSelection(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        SetSelectedIndexCore(index);
        OnSelectionChangeCommitted(EventArgs.Empty);
    }

    /// <summary>The arrow keys when the list is closed: they move and commit the selection immediately.</summary>
    private void StepSelection(int delta)
    {
        if (_items.Count == 0) return;

        var current = _selectedIndex >= 0 ? _selectedIndex : (delta > 0 ? -1 : _items.Count);
        var target = Math.Clamp(current + delta, 0, _items.Count - 1);

        if (target != _selectedIndex) CommitSelection(target);
    }

    private int VisibleDropDownRows() => Math.Max(Math.Min(_maxDropDownItems, _items.Count), 1);

    private void MoveDropDownHighlight(int delta) => SetDropDownHighlight(Math.Clamp(_dropDownHighlight + delta, 0, _items.Count - 1));

    private void SetDropDownHighlight(int index)
    {
        if (_items.Count == 0) return;

        index = Math.Clamp(index, 0, _items.Count - 1);
        if (index == _dropDownHighlight) return;

        _dropDownHighlight = index;
        EnsureDropDownVisible(index);
        _dropDownPopup?.Invalidate();
    }

    private void EnsureDropDownVisible(int index)
    {
        var rows = VisibleDropDownRows();

        if (index < _dropDownTopIndex) _dropDownTopIndex = index;
        else if (index >= _dropDownTopIndex + rows) _dropDownTopIndex = index - rows + 1;
    }

    private void OpenDropDown()
    {
        if (_droppedDown || _items.Count == 0 || _dropDownStyle == ComboBoxStyle.Simple || !IsHandleCreated) return;

        _preDropDownSelectedIndex = _selectedIndex;
        _dropDownHighlight = _selectedIndex >= 0 ? _selectedIndex : 0;
        _dropDownTopIndex = 0;
        EnsureDropDownVisible(_dropDownHighlight);

        _droppedDown = true;
        _dropDownPopup ??= new DropDownPopup(this);

        Popups.Show(_dropDownPopup, this, DropDownScreenBounds());

        Invalidate(ButtonBounds);
        OnDropDown(EventArgs.Empty);
    }

    /// <summary>A combo destroyed or disposed with its list open takes the list and its caret timer with it.</summary>
    internal override void OnHandleDestroyedInternal()
    {
        base.OnHandleDestroyedInternal();

        _droppedDown = false;
        if (_dropDownPopup is not null) Popups.Close(_dropDownPopup);
        EditCaretBlinker.Stop(this);
    }

    private void CloseDropDown(bool revert)
    {
        if (!_droppedDown) return;

        _droppedDown = false;
        if (_dropDownPopup is not null) Popups.Close(_dropDownPopup);

        if (revert) SetSelectedIndexCore(_preDropDownSelectedIndex);

        Invalidate(ButtonBounds);
        OnDropDownClosed(EventArgs.Empty);
    }

    /// <summary>The popup's bounds in screen coordinates: directly below the control, as wide as DropDownWidth or the control itself.</summary>
    private Rectangle DropDownScreenBounds()
    {
        var rows = VisibleDropDownRows();
        var rowHeight = Math.Max(_itemHeight, 1);
        var contentHeight = rows * rowHeight;

        var height = _dropDownHeight > 0 ? Math.Min(contentHeight, _dropDownHeight) : contentHeight;
        if (_integralHeight) height = Math.Max(height / rowHeight, 1) * rowHeight;

        var width = _dropDownWidth > 0 ? _dropDownWidth : Width;
        var topLeft = PointToScreen(new Point(0, Height));

        return new Rectangle(topLeft.X, topLeft.Y, Math.Max(width, 1), height);
    }

    private int IndexFromDropDownPoint(Point point)
    {
        if (point.Y < 0) return -1;

        var index = _dropDownTopIndex + point.Y / Math.Max(_itemHeight, 1);
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private void PaintDropDownItems(Graphics graphics, Rectangle bounds)
    {
        using (var back = new SolidBrush(SystemColors.Window)) graphics.FillRectangle(back, bounds);

        if (_items.Count == 0) return;

        var rowHeight = Math.Max(_itemHeight, 1);
        var rows = bounds.Height / rowHeight + 1;

        for (var row = 0; row <= rows; row++)
        {
            var index = _dropDownTopIndex + row;
            if (index >= _items.Count) break;

            var itemBounds = new Rectangle(bounds.X, bounds.Y + row * rowHeight, bounds.Width, rowHeight);
            if (itemBounds.Top >= bounds.Bottom) break;

            PaintDropDownItem(graphics, index, itemBounds);
        }
    }

    private void PaintDropDownItem(Graphics graphics, int index, Rectangle bounds)
    {
        var highlighted = index == _dropDownHighlight;
        var foreColor = highlighted ? SystemColors.HighlightText : EffectiveForeColor;
        var backColor = highlighted ? SystemColors.Highlight : SystemColors.Window;

        if (_drawMode != DrawMode.Normal)
        {
            var state = highlighted ? DrawItemState.Selected : DrawItemState.None;

            using var args = new DrawItemEventArgs(graphics, Font, bounds, index, state, foreColor, backColor);
            OnDrawItem(args);
            return;
        }

        using (var brush = new SolidBrush(backColor)) graphics.FillRectangle(brush, bounds);

        var text = GetItemText(_items[index]);
        var textBounds = new Rectangle(bounds.X + 2, bounds.Y, Math.Max(bounds.Width - 4, 0), bounds.Height);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, text, Font, textBounds, foreColor, flags);
    }

    /// <summary>
    /// The borderless popup that shows the list: it only paints and hit-tests
    /// (see <see cref="PaintDropDownItems"/>, <see cref="IndexFromDropDownPoint"/>
    /// on the owner); the owner keeps the focus and the keyboard throughout.
    /// </summary>
    private sealed class DropDownPopup : Control
    {
        private readonly ComboBox _owner;

        public DropDownPopup(ComboBox owner)
        {
            _owner = owner;
            SetTopLevel(true);
            SetStyle(ControlStyles.UserPaint, false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // No frame of its own: a 1px classic list border is left out so the popup's
                // ClientRectangle matches its screen bounds exactly, pixel for pixel.
                var parameters = base.CreateParams;
                return parameters;
            }
        }

        internal override bool TakesKeyboardWhileOpen => false;

        internal override void DismissPopup() => _owner.CloseDropDown(revert: false);

        internal override void DefaultPaint(PaintEventArgs e) => _owner.PaintDropDownItems(e.Graphics, ClientRectangle);

        internal override void DefaultMouseMove(MouseEventArgs e)
        {
            var index = _owner.IndexFromDropDownPoint(e.Location);
            if (index < 0 || index == _owner._dropDownHighlight) return;

            _owner._dropDownHighlight = index;
            Invalidate();
        }

        internal override void DefaultMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var index = _owner.IndexFromDropDownPoint(e.Location);
            if (index < 0) return;

            _owner.CommitSelection(index);
            _owner.CloseDropDown(revert: false);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var rows = Math.Sign(e.Delta);
            if (rows == 0) return;

            _owner._dropDownTopIndex = Math.Clamp(_owner._dropDownTopIndex - rows, 0, Math.Max(_owner._items.Count - _owner.VisibleDropDownRows(), 0));
            Invalidate();
        }
    }
}
