using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public partial class DataGrid
{
    public bool BeginEdit(DataGridColumnStyle gridColumn, int rowNumber) => throw new PlatformNotSupportedException();

    public void BeginInit() => throw new PlatformNotSupportedException();

    protected virtual void CancelEditing() => throw new PlatformNotSupportedException();

    public void Collapse(int row) => throw new PlatformNotSupportedException();

    protected virtual void ColumnStartedEditing(Rectangle bounds) => throw new PlatformNotSupportedException();

    protected virtual void ColumnStartedEditing(Control editingControl) => throw new PlatformNotSupportedException();

    protected virtual DataGridColumnStyle CreateGridColumn(PropertyDescriptor prop, bool isDefault) => throw new PlatformNotSupportedException();

    protected virtual DataGridColumnStyle CreateGridColumn(PropertyDescriptor prop) => throw new PlatformNotSupportedException();

    public bool EndEdit(DataGridColumnStyle gridColumn, int rowNumber, bool shouldAbort) => throw new PlatformNotSupportedException();

    public void EndInit() => throw new PlatformNotSupportedException();

    public void Expand(int row) => throw new PlatformNotSupportedException();

    public Rectangle GetCellBounds(int row, int col) => throw new PlatformNotSupportedException();

    public Rectangle GetCellBounds(DataGridCell dgc) => throw new PlatformNotSupportedException();

    public Rectangle GetCurrentCellBounds() => throw new PlatformNotSupportedException();

    protected virtual string GetOutputTextDelimiter() => throw new PlatformNotSupportedException();

    protected virtual void GridHScrolled(object sender, ScrollEventArgs se) => throw new PlatformNotSupportedException();

    protected virtual void GridVScrolled(object sender, ScrollEventArgs se) => throw new PlatformNotSupportedException();

    public DataGrid.HitTestInfo HitTest(int x, int y) => throw new PlatformNotSupportedException();

    // Hides Control's internal (Control, Point) HitTest(Point) on purpose:
    // Microsoft's DataGrid.HitTest(Point) returns its own HitTestInfo.
    public new DataGrid.HitTestInfo HitTest(Point position) => throw new PlatformNotSupportedException();

    public bool IsExpanded(int rowNumber) => throw new PlatformNotSupportedException();

    public bool IsSelected(int row) => throw new PlatformNotSupportedException();

    public void NavigateBack() => throw new PlatformNotSupportedException();

    public void NavigateTo(int rowNumber, string relationName) => throw new PlatformNotSupportedException();

    protected virtual void OnAllowNavigationChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected void OnBackButtonClicked(object sender, EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnBackgroundColorChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnBorderStyleChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnCaptionVisibleChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnCurrentCellChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnDataSourceChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnFlatModeChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected void OnNavigate(NavigateEventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnParentRowsLabelStyleChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnParentRowsVisibleChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnReadOnlyChanged(EventArgs e) => throw new PlatformNotSupportedException();

    protected void OnRowHeaderClick(EventArgs e) => throw new PlatformNotSupportedException();

    protected void OnScroll(EventArgs e) => throw new PlatformNotSupportedException();

    protected void OnShowParentDetailsButtonClicked(object sender, EventArgs e) => throw new PlatformNotSupportedException();

    protected bool ProcessGridKey(KeyEventArgs ke) => throw new PlatformNotSupportedException();

    protected bool ProcessTabKey(Keys keyData) => throw new PlatformNotSupportedException();

    public void ResetAlternatingBackColor() => throw new PlatformNotSupportedException();

    public void ResetGridLineColor() => throw new PlatformNotSupportedException();

    public void ResetHeaderBackColor() => throw new PlatformNotSupportedException();

    public void ResetHeaderFont() => throw new PlatformNotSupportedException();

    public void ResetHeaderForeColor() => throw new PlatformNotSupportedException();

    public void ResetLinkColor() => throw new PlatformNotSupportedException();

    public void ResetLinkHoverColor() => throw new PlatformNotSupportedException();

    protected void ResetSelection() => throw new PlatformNotSupportedException();

    public void ResetSelectionBackColor() => throw new PlatformNotSupportedException();

    public void ResetSelectionForeColor() => throw new PlatformNotSupportedException();

    public void Select(int row) => throw new PlatformNotSupportedException();

    public void SetDataBinding(object dataSource, string dataMember) => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeAlternatingBackColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeBackgroundColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeCaptionBackColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeCaptionForeColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeGridLineColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeHeaderBackColor() => throw new PlatformNotSupportedException();

    protected bool ShouldSerializeHeaderFont() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeHeaderForeColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeLinkHoverColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeParentRowsBackColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeParentRowsForeColor() => throw new PlatformNotSupportedException();

    protected bool ShouldSerializePreferredRowHeight() => throw new PlatformNotSupportedException();

    protected bool ShouldSerializeSelectionBackColor() => throw new PlatformNotSupportedException();

    protected virtual bool ShouldSerializeSelectionForeColor() => throw new PlatformNotSupportedException();

    public void SubObjectsSiteChange(bool site) => throw new PlatformNotSupportedException();

    public void UnSelect(int row) => throw new PlatformNotSupportedException();
}
