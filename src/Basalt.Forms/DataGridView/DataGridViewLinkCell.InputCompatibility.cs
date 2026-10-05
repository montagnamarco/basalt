namespace System.Windows.Forms;

public partial class DataGridViewLinkCell
{
    private bool _hovered;

    protected override bool KeyUpUnsharesRow(KeyEventArgs e, int rowIndex) =>
        HasSharedOwningRow && e.KeyCode is (Keys.Space or Keys.Enter) && TrackVisitedState;

    protected override bool MouseDownUnsharesRow(DataGridViewCellMouseEventArgs e) =>
        HasSharedOwningRow && e.Button is (MouseButtons.Left or MouseButtons.None) && ContentBounds.Contains(e.X, e.Y);

    protected override bool MouseLeaveUnsharesRow(int rowIndex) => HasSharedOwningRow && (_hovered || _pressed);

    protected override bool MouseMoveUnsharesRow(DataGridViewCellMouseEventArgs e) =>
        HasSharedOwningRow && _hovered != ContentBounds.Contains(e.X, e.Y);

    protected override bool MouseUpUnsharesRow(DataGridViewCellMouseEventArgs e) => HasSharedOwningRow && _pressed;

    protected override void OnKeyUp(KeyEventArgs e, int rowIndex)
    {
        if (e.KeyCode is not (Keys.Space or Keys.Enter) || e.Modifiers != Keys.None) return;

        if (TrackVisitedState) LinkVisited = true;
        DataGridView?.InvalidateCellCore(this);
        e.Handled = true;
        if (ColumnIndex < 0 || rowIndex < 0) return;

        var cellArgs = new DataGridViewCellEventArgs(ColumnIndex, rowIndex);
        RaiseCellClick(cellArgs);
        RaiseCellContentClick(cellArgs);
    }

    protected override void OnMouseLeave(int rowIndex)
    {
        if (!_hovered && !_pressed) return;
        _hovered = false;
        _pressed = false;
        DataGridView?.InvalidateCellCore(this);
    }

    protected override void OnMouseMove(DataGridViewCellMouseEventArgs e)
    {
        var hovered = ContentBounds.Contains(e.X, e.Y);
        if (_hovered == hovered) return;
        _hovered = hovered;
        DataGridView?.InvalidateCellCore(this);
    }
}
