using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class DataGridBoolColumn : DataGridColumnStyle
{
    public DataGridBoolColumn() => throw new PlatformNotSupportedException();

    public DataGridBoolColumn(PropertyDescriptor prop) => throw new PlatformNotSupportedException();

    public DataGridBoolColumn(PropertyDescriptor prop, bool isDefault) => throw new PlatformNotSupportedException();

    public bool AllowNull
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object FalseValue
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object NullValue
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object TrueValue
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public event EventHandler? AllowNullChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? FalseValueChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? TrueValueChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    protected override void Abort(int rowNum) => throw new PlatformNotSupportedException();

    protected override bool Commit(CurrencyManager dataSource, int rowNum) => throw new PlatformNotSupportedException();

    protected override void Edit(CurrencyManager source, int rowNum, Rectangle bounds, bool readOnly, string displayText, bool cellIsVisible) => throw new PlatformNotSupportedException();

    protected override int GetMinimumHeight() => throw new PlatformNotSupportedException();

    protected override int GetPreferredHeight(Graphics g, object value) => throw new PlatformNotSupportedException();

    protected override Size GetPreferredSize(Graphics g, object value) => throw new PlatformNotSupportedException();

    protected override void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum, bool alignToRight) => throw new PlatformNotSupportedException();

    protected override void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum) => throw new PlatformNotSupportedException();
}
