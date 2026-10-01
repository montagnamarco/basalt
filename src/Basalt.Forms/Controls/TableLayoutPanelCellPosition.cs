namespace System.Windows.Forms;

/// <summary>A cell in a <see cref="TableLayoutPanel"/>, by its column and row.</summary>
public struct TableLayoutPanelCellPosition : IEquatable<TableLayoutPanelCellPosition>
{
    public TableLayoutPanelCellPosition(int column, int row)
    {
        Column = column;
        Row = row;
    }

    public int Column { get; set; }

    public int Row { get; set; }

    public override bool Equals(object? other) => other is TableLayoutPanelCellPosition position && Equals(position);

    public bool Equals(TableLayoutPanelCellPosition other) => Column == other.Column && Row == other.Row;

    public override int GetHashCode() => HashCode.Combine(Column, Row);

    public override string ToString() => $"{Column},{Row}";

    public static bool operator ==(TableLayoutPanelCellPosition p1, TableLayoutPanelCellPosition p2) => p1.Equals(p2);

    public static bool operator !=(TableLayoutPanelCellPosition p1, TableLayoutPanelCellPosition p2) => !p1.Equals(p2);
}
