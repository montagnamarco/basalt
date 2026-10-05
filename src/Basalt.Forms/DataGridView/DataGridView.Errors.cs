using System.ComponentModel;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private bool _showCellErrors = true;
    private bool _showRowErrors = true;

    [DefaultValue(true)]
    public bool ShowCellErrors
    {
        get => _showCellErrors;
        set
        {
            if (_showCellErrors == value) return;
            _showCellErrors = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowRowErrors
    {
        get => _showRowErrors;
        set
        {
            if (_showRowErrors == value) return;
            _showRowErrors = value;
            Invalidate();
        }
    }
}
