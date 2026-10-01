namespace System.Windows.Forms;

/// <summary>The four edges a cell or header draws its border with.</summary>
public sealed class DataGridViewAdvancedBorderStyle : ICloneable
{
    private DataGridViewAdvancedCellBorderStyle _top = DataGridViewAdvancedCellBorderStyle.None;
    private DataGridViewAdvancedCellBorderStyle _bottom = DataGridViewAdvancedCellBorderStyle.None;
    private DataGridViewAdvancedCellBorderStyle _left = DataGridViewAdvancedCellBorderStyle.None;
    private DataGridViewAdvancedCellBorderStyle _right = DataGridViewAdvancedCellBorderStyle.None;

    public DataGridViewAdvancedBorderStyle()
    {
    }

    /// <summary>Setting All assigns every edge at once; reading it answers the common edge, or NotSet when the edges differ.</summary>
    public DataGridViewAdvancedCellBorderStyle All
    {
        get => _top == _bottom && _bottom == _left && _left == _right ? _top : DataGridViewAdvancedCellBorderStyle.NotSet;
        set
        {
            _top = value;
            _bottom = value;
            _left = value;
            _right = value;
        }
    }

    public DataGridViewAdvancedCellBorderStyle Bottom
    {
        get => _bottom;
        set => _bottom = value;
    }

    public DataGridViewAdvancedCellBorderStyle Left
    {
        get => _left;
        set => _left = value;
    }

    public DataGridViewAdvancedCellBorderStyle Right
    {
        get => _right;
        set => _right = value;
    }

    public DataGridViewAdvancedCellBorderStyle Top
    {
        get => _top;
        set => _top = value;
    }

    object ICloneable.Clone() => CloneCore();

    /// <summary>A copy carrying the same four edges, for the painting code that adjusts one edge of a borrowed placeholder instance.</summary>
    internal DataGridViewAdvancedBorderStyle CloneCore() => new()
    {
        _top = _top,
        _bottom = _bottom,
        _left = _left,
        _right = _right,
    };

    public override bool Equals(object? other) =>
        other is DataGridViewAdvancedBorderStyle style &&
        style._top == _top && style._bottom == _bottom && style._left == _left && style._right == _right;

    public override int GetHashCode() => HashCode.Combine(_top, _bottom, _left, _right);

    public override string ToString() =>
        $"DataGridViewAdvancedBorderStyle {{ Top={_top}, Bottom={_bottom}, Left={_left}, Right={_right} }}";
}
