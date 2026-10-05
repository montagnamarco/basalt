using System.ComponentModel;

namespace System.Windows.Forms;

public partial class DataGridView
{
    public int GetCellCount(DataGridViewElementStates includeFilter)
    {
        const DataGridViewElementStates validStates = DataGridViewElementStates.Displayed
            | DataGridViewElementStates.Frozen | DataGridViewElementStates.ReadOnly
            | DataGridViewElementStates.Resizable | DataGridViewElementStates.ResizableSet
            | DataGridViewElementStates.Selected | DataGridViewElementStates.Visible;
        if ((includeFilter & ~validStates) != 0)
        {
            throw new InvalidEnumArgumentException(nameof(includeFilter), (int)includeFilter, typeof(DataGridViewElementStates));
        }

        var count = 0;
        foreach (DataGridViewRow row in Rows)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                if ((cell.InheritedState & includeFilter) == includeFilter) count++;
            }
        }

        return count;
    }
}
