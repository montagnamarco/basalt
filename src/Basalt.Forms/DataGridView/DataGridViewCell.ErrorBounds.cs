using System.Drawing;

namespace System.Windows.Forms;

public partial class DataGridViewButtonCell
{
    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        GetStandardErrorIconBounds(cellStyle, rowIndex);
}

public partial class DataGridViewCheckBoxCell
{
    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        GetStandardErrorIconBounds(cellStyle, rowIndex);
}

public partial class DataGridViewImageCell
{
    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        GetStandardErrorIconBounds(cellStyle, rowIndex);
}

public partial class DataGridViewLinkCell
{
    protected override Rectangle GetErrorIconBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        GetStandardErrorIconBounds(cellStyle, rowIndex);
}
