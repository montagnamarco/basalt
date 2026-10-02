using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The Form members a print preview dialog hides from the designer that
/// describe the state of the window itself: where it is, how big, and
/// whether it is enabled, visible or reachable by Tab.
/// </summary>
public partial class PrintPreviewDialog
{
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool Enabled
    {
        get => base.Enabled;
        set => base.Enabled = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool IsMdiContainer
    {
        get => base.IsMdiContainer;
        set => base.IsMdiContainer = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool KeyPreview
    {
        get => base.KeyPreview;
        set => base.KeyPreview = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Point Location
    {
        get => base.Location;
        set => base.Location = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Padding Margin
    {
        get => base.Margin;
        set => base.Margin = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size MaximumSize
    {
        get => base.MaximumSize;
        set => base.MaximumSize = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size MinimumSize
    {
        get => base.MinimumSize;
        set => base.MinimumSize = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size Size
    {
        get => base.Size;
        set => base.Size = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new FormStartPosition StartPosition
    {
        get => base.StartPosition;
        set => base.StartPosition = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new object? Tag
    {
        get => base.Tag;
        set => base.Tag = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool Visible
    {
        get => base.Visible;
        set => base.Visible = value;
    }
}
