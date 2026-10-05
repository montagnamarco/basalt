using System.ComponentModel;

namespace System.Windows.Forms;

public partial class TreeView
{
    private bool _rightToLeftLayout;
    private static readonly object s_treeRightToLeftLayoutChangedEvent = new();

    [DefaultValue(false)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (_rightToLeftLayout == value) return;
            _rightToLeftLayout = value;
            Invalidate();
            OnRightToLeftLayoutChanged(EventArgs.Empty);
        }
    }

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_treeRightToLeftLayoutChangedEvent, value);
        remove => Events.RemoveHandler(s_treeRightToLeftLayoutChangedEvent, value);
    }

    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) =>
        ((EventHandler?)Events[s_treeRightToLeftLayoutChangedEvent])?.Invoke(this, e);

    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}
