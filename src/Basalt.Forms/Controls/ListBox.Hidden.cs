using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members ListBox re-declares only to keep the public shape Microsoft
/// declares on this type: overrides of ListControl's and Control's own
/// virtual hooks that this implementation needs no extra behaviour for
/// beyond what the base class already does.
/// </summary>
/// <remarks>
/// OnChangeUICues, RescaleConstantsForDpi and WmReflectCommand are skipped:
/// the first two override Control members p0-control.txt lists as not yet
/// implemented, and WmReflectCommand only makes sense once WndProc (also
/// missing there) reflects WM_COMMAND back from the parent.
/// </remarks>
public partial class ListBox
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override Rectangle GetScaledBounds(Rectangle bounds, SizeF factor, BoundsSpecified specified) =>
        base.GetScaledBounds(bounds, factor, specified);

    protected override void OnDataSourceChanged(EventArgs e) => base.OnDataSourceChanged(e);

    protected override void OnDisplayMemberChanged(EventArgs e) => base.OnDisplayMemberChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnParentChanged(EventArgs e) => base.OnParentChanged(e);

    protected override void OnResize(EventArgs e) => base.OnResize(e);

    protected override void OnSelectedValueChanged(EventArgs e) => base.OnSelectedValueChanged(e);

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) => base.ScaleControl(factor, specified);
}
