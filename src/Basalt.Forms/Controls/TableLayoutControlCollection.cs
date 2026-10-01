using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A <see cref="TableLayoutPanel"/>'s children, with the overload that places one at a cell directly.</summary>
[ListBindable(false)]
public class TableLayoutControlCollection : Control.ControlCollection
{
    public TableLayoutControlCollection(TableLayoutPanel container)
        : base(container)
    {
    }

    public TableLayoutPanel Container => (TableLayoutPanel)Owner;

    /// <summary>Adds the control and fixes its column and row, instead of leaving it for automatic placement.</summary>
    public virtual void Add(Control control, int column, int row)
    {
        Add(control);
        Container.SetColumn(control, column);
        Container.SetRow(control, row);
    }
}
