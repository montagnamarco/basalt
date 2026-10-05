namespace System.Windows.Forms;

public partial class DataGridViewCheckBoxCell
{
    private bool _pressed;
    private bool _mouseDownOnGlyph;
    private DataGridViewCellEventArgs? _lastContentClickArgs;

    protected override bool KeyDownUnsharesRow(KeyEventArgs e, int rowIndex) =>
        HasSharedOwningRow && e.KeyCode == Keys.Space;

    protected override bool KeyUpUnsharesRow(KeyEventArgs e, int rowIndex) =>
        HasSharedOwningRow && e.KeyCode == Keys.Space;

    protected override bool MouseDownUnsharesRow(DataGridViewCellMouseEventArgs e) =>
        HasSharedOwningRow && e.Button == MouseButtons.Left && ContentBounds.Contains(e.X, e.Y);

    protected override bool MouseEnterUnsharesRow(int rowIndex) => HasSharedOwningRow && _mouseDownOnGlyph;

    protected override bool MouseLeaveUnsharesRow(int rowIndex) => HasSharedOwningRow && _pressed;

    protected override bool MouseUpUnsharesRow(DataGridViewCellMouseEventArgs e) => HasSharedOwningRow && _pressed;

    protected override void OnContentDoubleClick(DataGridViewCellEventArgs e)
    {
        // This grid dispatches the ordinary content click on every release, including the
        // second release of a double-click. The same event args then reach this extra hook;
        // avoid a third toggle while still handling a direct double-click dispatch.
        if (ReferenceEquals(e, _lastContentClickArgs))
        {
            _lastContentClickArgs = null;
            return;
        }

        Toggle();
    }

    protected override void OnKeyDown(KeyEventArgs e, int rowIndex)
    {
        if (e.KeyCode != Keys.Space || e.Modifiers != Keys.None) return;
        SetPressed(true);
        e.Handled = true;
    }

    protected override void OnLeave(int rowIndex, bool throughMouseClick)
    {
        _mouseDownOnGlyph = false;
        SetPressed(false);
    }

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        _mouseDownOnGlyph = e.Button == MouseButtons.Left && ContentBounds.Contains(e.X, e.Y);
        SetPressed(_mouseDownOnGlyph);
    }

    protected override void OnMouseLeave(int rowIndex) => SetPressed(false);

    protected override void OnMouseMove(DataGridViewCellMouseEventArgs e) =>
        SetPressed(_mouseDownOnGlyph && e.Button == MouseButtons.Left && ContentBounds.Contains(e.X, e.Y));

    protected override void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
        _mouseDownOnGlyph = false;
        SetPressed(false);
    }

    private void SetPressed(bool value)
    {
        if (_pressed == value) return;
        _pressed = value;
        DataGridView?.InvalidateCellCore(this);
    }
}
