using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// DataGridView declares these Control hooks in the WinForms API. The base
/// implementations already deliver input, focus, appearance and lifetime
/// changes to this grid's event and layout pipeline.
/// </summary>
public partial class DataGridView
{
    protected override bool IsInputChar(char charCode) => base.IsInputChar(charCode);

    protected override void OnCursorChanged(EventArgs e) => base.OnCursorChanged(e);

    protected override void OnDoubleClick(EventArgs e) => base.OnDoubleClick(e);

    protected override void OnEnabledChanged(EventArgs e) => base.OnEnabledChanged(e);

    protected override void OnEnter(EventArgs e) => base.OnEnter(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnForeColorChanged(EventArgs e) => base.OnForeColorChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnLeave(EventArgs e) => base.OnLeave(e);

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnMouseClick(MouseEventArgs e) => base.OnMouseClick(e);

    protected override void OnMouseDoubleClick(MouseEventArgs e) => base.OnMouseDoubleClick(e);

    protected override void OnMouseEnter(EventArgs e) => base.OnMouseEnter(e);

    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);

    protected override void OnValidating(CancelEventArgs e) => base.OnValidating(e);

    protected override void OnVisibleChanged(EventArgs e) => base.OnVisibleChanged(e);
}
