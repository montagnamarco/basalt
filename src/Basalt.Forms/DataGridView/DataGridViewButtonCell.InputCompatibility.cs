namespace System.Windows.Forms;

public partial class DataGridViewButtonCell
{
    protected override bool KeyDownUnsharesRow(KeyEventArgs e, int rowIndex) =>
        HasSharedOwningRow && e.KeyCode == Keys.Space;

    protected override bool KeyUpUnsharesRow(KeyEventArgs e, int rowIndex) =>
        HasSharedOwningRow && e.KeyCode == Keys.Space && _pressed;

    protected override bool MouseDownUnsharesRow(DataGridViewCellMouseEventArgs e) =>
        HasSharedOwningRow && e.Button == MouseButtons.Left;

    protected override bool MouseEnterUnsharesRow(int rowIndex) => false;

    protected override bool MouseLeaveUnsharesRow(int rowIndex) => HasSharedOwningRow && _pressed;

    protected override bool MouseUpUnsharesRow(DataGridViewCellMouseEventArgs e) => HasSharedOwningRow && _pressed;

    protected override void OnKeyDown(KeyEventArgs e, int rowIndex)
    {
        if (e.KeyCode != Keys.Space || e.Modifiers != Keys.None) return;
        if (!_pressed)
        {
            _pressed = true;
            DataGridView?.InvalidateCellCore(this);
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e, int rowIndex)
    {
        if (e.KeyCode != Keys.Space || !_pressed) return;
        _pressed = false;
        DataGridView?.InvalidateCellCore(this);
        e.Handled = true;
        if (ColumnIndex < 0 || rowIndex < 0) return;

        var cellArgs = new DataGridViewCellEventArgs(ColumnIndex, rowIndex);
        RaiseCellClick(cellArgs);
        RaiseCellContentClick(cellArgs);
    }

    protected override void OnLeave(int rowIndex, bool throughMouseClick) => ResetPressed();

    protected override void OnMouseLeave(int rowIndex) => ResetPressed();

    protected override void OnMouseMove(DataGridViewCellMouseEventArgs e)
    {
        if (e.Button == MouseButtons.None) ResetPressed();
    }

    private void ResetPressed()
    {
        if (!_pressed) return;
        _pressed = false;
        DataGridView?.InvalidateCellCore(this);
    }
}
