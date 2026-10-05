using System.ComponentModel;

namespace System.Windows.Forms;

public partial class ListView
{
    public void AutoResizeColumn(int columnIndex, ColumnHeaderAutoResizeStyle headerAutoResize)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
        if (!Enum.IsDefined(headerAutoResize))
            throw new InvalidEnumArgumentException(nameof(headerAutoResize), (int)headerAutoResize, typeof(ColumnHeaderAutoResizeStyle));

        if (headerAutoResize == ColumnHeaderAutoResizeStyle.None) return;

        var column = _columns[columnIndex];
        var width = TextRenderer.MeasureText(column.Text, Font).Width + 8;
        if (column.ImageList is { } images && (column.ImageIndex >= 0 || column.ImageKey.Length > 0))
            width += images.ImageSize.Width + 3;

        if (headerAutoResize == ColumnHeaderAutoResizeStyle.ColumnContent)
        {
            width = 0;
            foreach (var item in _items)
            {
                if (columnIndex >= item.SubItems.Count) continue;
                var subItem = item.SubItems[columnIndex];
                var contentWidth = TextRenderer.MeasureText(subItem.Text, subItem.Font ?? item.Font).Width + 8;
                if (columnIndex == 0 && item.ImageIndex >= 0 && SmallImageList is { } itemImages)
                    contentWidth += itemImages.ImageSize.Width + 4;
                width = Math.Max(width, contentWidth);
            }
        }

        column.Width = width;
    }

    public void AutoResizeColumns(ColumnHeaderAutoResizeStyle headerAutoResize)
    {
        if (!Enum.IsDefined(headerAutoResize))
            throw new InvalidEnumArgumentException(nameof(headerAutoResize), (int)headerAutoResize, typeof(ColumnHeaderAutoResizeStyle));

        for (var column = 0; column < _columns.Count; column++) AutoResizeColumn(column, headerAutoResize);
    }
}
