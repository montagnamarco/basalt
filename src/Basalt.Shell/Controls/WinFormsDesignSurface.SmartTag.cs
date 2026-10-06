using Avalonia.Controls;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    private WinFormsSmartTag? _smartTag;
    private FormsControl? _smartTagControl;

    /// <summary>Shows (or hides, when either argument is null) the smart tag glyph and flyout
    /// for the given control. What tasks it offers is <see cref="WinFormsDesignerView"/>'s
    /// decision; this class only hosts and positions the control it is handed, the same split
    /// <see cref="ShowInlineMenuEditor"/>/<see cref="ShowInlineToolStripEditor"/> use.</summary>
    internal void ShowSmartTag(WinFormsSmartTag? tag, FormsControl? control)
    {
        if (_smartTag is not null)
        {
            // Closed before anything else: Show() only resets the flyout's open/closed state
            // when the new selection's key differs from the one it last built for, so without
            // this, reselecting the same control after selecting away from it (which hides the
            // glyph via ShowSmartTag(null, null) rather than calling Show() again) would
            // resurrect a flyout the user had left open.
            _smartTag.Close();
            _host.Children.Remove(_smartTag);
        }

        _smartTag = tag;
        _smartTagControl = control;
        if (tag is null || control is null || !BelongsToRoot(control))
        {
            return;
        }

        PositionSmartTag();
        _host.Children.Add(tag);
    }

    private void PositionSmartTag()
    {
        if (_smartTag is null || _smartTagControl is null || !BelongsToRoot(_smartTagControl))
        {
            return;
        }

        // Outside the selection, to the right of the top-right resize handle with a gap wide
        // enough to clear that handle's own hit box (HandleAt tests +/- HandleSize around its
        // centre): the glyph is a topmost Button, so sitting on top of the handle - as it used
        // to, centred exactly on bounds.TopRight - swallowed the press and broke that resize.
        // Flips to just inside the selection, left of the corner, when there is no room to the
        // right, the way Visual Studio's own glyph does near the edge of the design surface.
        var box = BoundsOnHost(_smartTagControl);
        var clearance = HandleSize + 2;
        var left = box.Right + clearance;
        if (left + WinFormsSmartTag.GlyphSize > _host.Width)
        {
            left = box.Right - clearance - WinFormsSmartTag.GlyphSize;
        }

        Canvas.SetLeft(_smartTag, left);
        Canvas.SetTop(_smartTag, box.Y - WinFormsSmartTag.GlyphSize / 2);
    }
}
