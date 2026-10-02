using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class DataGridTextBoxColumn : DataGridColumnStyle
{
    public DataGridTextBoxColumn() => throw new PlatformNotSupportedException();

    public DataGridTextBoxColumn(PropertyDescriptor prop) => throw new PlatformNotSupportedException();

    public DataGridTextBoxColumn(PropertyDescriptor prop, string format) => throw new PlatformNotSupportedException();

    public DataGridTextBoxColumn(PropertyDescriptor prop, bool isDefault) => throw new PlatformNotSupportedException();

    public DataGridTextBoxColumn(PropertyDescriptor prop, string format, bool isDefault) => throw new PlatformNotSupportedException();

    public string Format
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public IFormatProvider FormatInfo
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public override PropertyDescriptor PropertyDescriptor
    {
        set => throw new PlatformNotSupportedException();
    }

    public virtual TextBox TextBox => throw new PlatformNotSupportedException();

    protected override void Abort(int rowNum) => throw new PlatformNotSupportedException();

    protected override bool Commit(CurrencyManager dataSource, int rowNum) => throw new PlatformNotSupportedException();

    protected override void Edit(CurrencyManager source, int rowNum, Rectangle bounds, bool readOnly, string displayText, bool cellIsVisible) => throw new PlatformNotSupportedException();

    protected void EndEdit() => throw new PlatformNotSupportedException();

    protected override int GetMinimumHeight() => throw new PlatformNotSupportedException();

    protected override int GetPreferredHeight(Graphics g, object value) => throw new PlatformNotSupportedException();

    protected override Size GetPreferredSize(Graphics g, object value) => throw new PlatformNotSupportedException();

    protected void HideEditBox() => throw new PlatformNotSupportedException();

    protected override void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum, bool alignToRight) => throw new PlatformNotSupportedException();

    protected override void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum) => throw new PlatformNotSupportedException();

    protected void PaintText(Graphics g, Rectangle bounds, string text, bool alignToRight) => throw new PlatformNotSupportedException();

    protected void PaintText(Graphics g, Rectangle textBounds, string text, Brush backBrush, Brush foreBrush, bool alignToRight) => throw new PlatformNotSupportedException();
}
