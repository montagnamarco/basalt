using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

/// <summary>
/// The native parts CheckedListBox adds on top of ListBox: a check box drawn
/// in front of every item's text, and the mouse and keyboard gestures that
/// toggle it.
/// </summary>
/// <remarks>
/// ListBox paints and tracks the mouse and keyboard through internal
/// DefaultPaint/DefaultMouseDown/DefaultKeyDown hooks (it is a native
/// control), and every one of them is declared internal virtual in Control,
/// so CheckedListBox overrides them again here rather than needing a new
/// hook. The one gap: ListBox's own keyboard-focused item (_focusedIndex) is
/// a private field with no accessor, so under SelectionMode.None - which has
/// no selection to read the current item back from - CheckedListBox tracks
/// its own current item instead of reusing ListBox's arrow-key handling. A
/// protected or internal FocusedIndex accessor on ListBox would remove that
/// small duplication.
/// </remarks>
public partial class CheckedListBox
{
    private const int CheckBoxSize = 13;
    private const int CheckBoxMargin = 2;
    private const int CheckBoxTextGap = 4;

    /// <summary>
    /// The item under keyboard or mouse attention right now. Mirrors
    /// SelectedIndex under SelectionMode.One; under SelectionMode.None,
    /// where nothing is ever selected, this is the only record of it.
    /// </summary>
    private int _currentIndex = -1;

    private Color EffectiveBackColor => CollectionVisualAppearance.Field(this,
        ShouldSerializeBackColor() ? BackColor : SystemColors.Window);

    private Color EffectiveForeColor => CollectionVisualAppearance.Text(this, Enabled
        ? (ShouldSerializeForeColor() ? ForeColor : SystemColors.WindowText)
        : SystemColors.GrayText);

    // --- Mouse -----------------------------------------------------------

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        var index = e.Button == MouseButtons.Left ? IndexFromPoint(e.Location) : -1;

        // Captured before the base changes the selection: CheckOnClick false
        // toggles only a second click on a row that was already current.
        var wasCurrent = index >= 0 && (SelectionMode == SelectionMode.One ? GetSelected(index) : index == _currentIndex);

        base.DefaultMouseDown(e);

        if (index < 0) return;

        _currentIndex = index;
        if (CheckOnClick || wasCurrent) ToggleCheck(index);
    }

    // --- Keyboard ----------------------------------------------------------

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (SelectionMode == SelectionMode.None) MoveCurrentForNoneMode(e);
        else
        {
            base.DefaultKeyDown(e);
            _currentIndex = SelectedIndex;
        }

        if (e.KeyCode != Keys.Space || Items.Count == 0) return;

        var index = SelectionMode == SelectionMode.One ? SelectedIndex : _currentIndex;
        if (index < 0) return;

        ToggleCheck(index);
        e.Handled = true;
    }

    /// <summary>The minimal Up/Down/Home/End/PageUp/PageDown navigation ListBox gives SelectionMode.None, reimplemented here (see remarks).</summary>
    private void MoveCurrentForNoneMode(KeyEventArgs e)
    {
        if (Items.Count == 0) return;

        var current = _currentIndex >= 0 ? _currentIndex : 0;
        var page = Math.Max(VisibleItemCountForNoneMode() - 1, 1);

        int? target = e.KeyCode switch
        {
            Keys.Up => Math.Max(current - 1, 0),
            Keys.Down => Math.Min(current + 1, Items.Count - 1),
            Keys.Home => 0,
            Keys.End => Items.Count - 1,
            Keys.PageUp => Math.Max(current - page, 0),
            Keys.PageDown => Math.Min(current + page, Items.Count - 1),
            _ => null,
        };

        if (target is not { } newIndex) return;

        _currentIndex = newIndex;
        EnsureCurrentVisible(newIndex);
        Invalidate();
        e.Handled = true;
    }

    private int VisibleItemCountForNoneMode() => Math.Max(ClientRectangle.Height / Math.Max(ItemHeight, 1), 1);

    private void EnsureCurrentVisible(int index)
    {
        var visible = VisibleItemCountForNoneMode();

        if (index < TopIndex) TopIndex = index;
        else if (index >= TopIndex + visible) TopIndex = index - visible + 1;
    }

    private void ToggleCheck(int index)
    {
        var next = GetItemCheckState(index) == CheckState.Unchecked ? CheckState.Checked : CheckState.Unchecked;
        SetItemCheckState(index, next);
    }

    // --- Painting ------------------------------------------------------------

    /// <summary>
    /// Paints every item itself (DrawMode is always Normal here) instead of
    /// calling the base: ListBox's own per-item painting is private, with no
    /// room to insert a check box glyph before the text.
    /// </summary>
    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        using (var back = new SolidBrush(EffectiveBackColor)) graphics.FillRectangle(back, client);

        for (var index = 0; index < Items.Count; index++)
        {
            var bounds = GetItemRectangle(index);
            if (!bounds.IntersectsWith(client)) continue;

            PaintCheckedItem(graphics, index, bounds);
        }
    }

    private void PaintCheckedItem(Graphics graphics, int index, Rectangle bounds)
    {
        var selected = SelectionMode == SelectionMode.One && GetSelected(index);
        var current = SelectionMode == SelectionMode.One ? index == SelectedIndex : index == _currentIndex;

        var foreColor = selected ? CollectionVisualAppearance.SelectionText(this, Focused) : EffectiveForeColor;
        var backColor = selected ? CollectionVisualAppearance.SelectionField(this, Focused) : EffectiveBackColor;

        using (var brush = new SolidBrush(backColor)) graphics.FillRectangle(brush, bounds);

        var checkBoxTop = bounds.Y + Math.Max((bounds.Height - CheckBoxSize) / 2, 0);
        var checkBox = new Rectangle(bounds.X + CheckBoxMargin, checkBoxTop, CheckBoxSize, CheckBoxSize);
        PaintCheckBox(graphics, checkBox, index);

        var textLeft = checkBox.Right + CheckBoxTextGap;
        var textBounds = new Rectangle(textLeft, bounds.Y, Math.Max(bounds.Right - textLeft, 0), bounds.Height);
        var text = GetItemText(Items[index]);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, text, Font, textBounds, foreColor, flags);

        if (Focused && current) ControlPaint.DrawFocusRectangle(graphics, bounds, foreColor, backColor);
    }

    private void PaintCheckBox(Graphics graphics, Rectangle checkBox, int index)
    {
        var checkState = GetItemCheckState(index);
        if (PortableVisualStylesEnabled)
        {
            PortableVisualStyles.PaintGlyph(graphics, checkBox,
                CollectionVisualAppearance.Palette(this), radio: false,
                selected: checkState == CheckState.Checked,
                mixed: checkState == CheckState.Indeterminate,
                pressed: false, enabled: Enabled);
            return;
        }

        var state = ButtonState.Normal;
        if (!Enabled) state |= ButtonState.Inactive;
        if (!ThreeDCheckBoxes) state |= ButtonState.Flat;

        if (checkState == CheckState.Indeterminate)
        {
            ControlPaint.DrawMixedCheckBox(graphics, checkBox, state | ButtonState.Checked);
        }
        else
        {
            if (checkState == CheckState.Checked) state |= ButtonState.Checked;
            ControlPaint.DrawCheckBox(graphics, checkBox, state);
        }
    }
}
