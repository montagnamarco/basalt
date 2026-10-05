using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// Legacy DataGrid has no portable implementation: the constructors and members
// retain their established PlatformNotSupportedException contract. The
// accessibility declarations below preserve API shape for compiled consumers.
[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultProperty("Header")]
public abstract class DataGridColumnStyle : Component, IDataGridColumnStyleEditingNotificationService
{
    public DataGridColumnStyle() => throw new PlatformNotSupportedException();

    public DataGridColumnStyle(PropertyDescriptor prop) => throw new PlatformNotSupportedException();

    public virtual HorizontalAlignment Alignment
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public virtual DataGridTableStyle DataGridTableStyle => throw new PlatformNotSupportedException();

    protected int FontHeight => throw new PlatformNotSupportedException();

    public virtual string HeaderText
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public AccessibleObject HeaderAccessibleObject => CreateHeaderAccessibleObject();

    protected virtual AccessibleObject CreateHeaderAccessibleObject() => throw new PlatformNotSupportedException();

    public string MappingName
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public virtual string NullText
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public virtual PropertyDescriptor PropertyDescriptor
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public virtual bool ReadOnly
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public virtual int Width
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public event EventHandler? AlignmentChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? FontChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? HeaderTextChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? MappingNameChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? NullTextChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? PropertyDescriptorChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? ReadOnlyChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? WidthChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    protected abstract void Abort(int rowNum);

    protected void BeginUpdate() => throw new PlatformNotSupportedException();

    protected void CheckValidDataSource(CurrencyManager value) => throw new PlatformNotSupportedException();

    protected virtual void ColumnStartedEditing(Control editingControl) => throw new PlatformNotSupportedException();

    // Microsoft's sheet keeps the member above protected; the interface is
    // satisfied explicitly, which is why it never shows up as public.
    void IDataGridColumnStyleEditingNotificationService.ColumnStartedEditing(Control editingControl) => throw new PlatformNotSupportedException();

    protected abstract bool Commit(CurrencyManager dataSource, int rowNum);

    protected virtual void ConcedeFocus() => throw new PlatformNotSupportedException();

    protected virtual void Edit(CurrencyManager source, int rowNum, Rectangle bounds, bool readOnly) => throw new PlatformNotSupportedException();

    protected virtual void Edit(CurrencyManager source, int rowNum, Rectangle bounds, bool readOnly, string displayText) => throw new PlatformNotSupportedException();

    protected abstract void Edit(CurrencyManager source, int rowNum, Rectangle bounds, bool readOnly, string displayText, bool cellIsVisible);

    protected void EndUpdate() => throw new PlatformNotSupportedException();

    protected virtual void EnterNullValue() => throw new PlatformNotSupportedException();

    protected virtual object GetColumnValueAtRow(CurrencyManager source, int rowNum) => throw new PlatformNotSupportedException();

    protected abstract int GetMinimumHeight();

    protected abstract int GetPreferredHeight(Graphics g, object value);

    protected abstract Size GetPreferredSize(Graphics g, object value);

    protected virtual void Invalidate() => throw new PlatformNotSupportedException();

    protected abstract void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum, bool alignToRight);

    protected virtual void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum, Brush backBrush, Brush foreBrush, bool alignToRight) => throw new PlatformNotSupportedException();

    protected abstract void Paint(Graphics g, Rectangle bounds, CurrencyManager source, int rowNum);

    protected virtual void ReleaseHostedControl() => throw new PlatformNotSupportedException();

    public void ResetHeaderText() => throw new PlatformNotSupportedException();

    protected virtual void SetColumnValueAtRow(CurrencyManager source, int rowNum, object value) => throw new PlatformNotSupportedException();

    protected virtual void SetDataGrid(DataGrid value) => throw new PlatformNotSupportedException();

    protected virtual void SetDataGridInColumn(DataGrid value) => throw new PlatformNotSupportedException();

    protected virtual void UpdateUI(CurrencyManager source, int rowNum, string displayText) => throw new PlatformNotSupportedException();

    [Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    protected class CompModSwitches
    {
        public CompModSwitches() => throw new PlatformNotSupportedException();

        // Static member: kept as Microsoft declares it, not throwing.
        public static TraceSwitch? DGEditColumnEditing { get; }
    }
}
