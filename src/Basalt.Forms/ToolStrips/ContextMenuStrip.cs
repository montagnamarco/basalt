using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A pop-up menu shown for a control, by right-clicking it (the only path
/// wired today: see the task's report for the Shift+F10 / Apps key and the
/// Control.ContextMenuStrip property it would still need).
/// </summary>
[DefaultEvent("Opening")]
public partial class ContextMenuStrip : ToolStripDropDownMenu, IContextMenuSource
{
    private Control? _sourceControl;

    public ContextMenuStrip()
    {
    }

    public ContextMenuStrip(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    /// <summary>The control the menu was last shown for; null once it has closed.</summary>
    [Browsable(false)]
    public Control? SourceControl => _sourceControl;

    /// <summary>
    /// Remembers which control a Show(control, ...) (inherited from ToolStripDropDown) was for, as
    /// SourceControl: an internal override, since Microsoft's own ContextMenuStrip does not add a
    /// new Show overload either, just this bookkeeping.
    /// </summary>
    internal override void ShowAt(Rectangle anchor, ToolStripDropDownDirection direction, Control? owner)
    {
        _sourceControl = owner;

        base.ShowAt(anchor, direction, owner);
    }

    /// <summary>
    /// Wired to Control.ContextMenuStripInternal, the existing hook a
    /// right-click on any control already calls: see the task's report for
    /// how a control is to be attached to a ContextMenuStrip without a new
    /// Control.ContextMenuStrip property.
    /// </summary>
    void IContextMenuSource.ShowFromControl(Control control, Point location)
    {
        // A right-click is a fresh mouse session: Windows hides mnemonic underlines again even when
        // an earlier menu was driven from the keyboard.
        KeyboardCuesVisible = false;
        var anchor = control.RectangleToScreen(new Rectangle(location, Size.Empty));
        ShowAt(anchor, ToolStripDropDownDirection.BelowRight, control);
    }

    /// <summary>
    /// Setting Visible = true without going through Show opens at the mouse position, the same
    /// fallback ToolStripDropDown.DefaultShowBounds already gives a bare Show() when there is no
    /// OwnerItem to anchor to -- matching WinForms, not an error, for a ContextMenuStrip either.
    /// </summary>
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
    }

    protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
    {
        base.OnClosed(e);
        _sourceControl = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _sourceControl = null;

        base.Dispose(disposing);
    }
}
