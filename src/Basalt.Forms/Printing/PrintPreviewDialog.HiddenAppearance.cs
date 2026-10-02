using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The Form members a print preview dialog hides from the designer that
/// Microsoft files under the "Appearance" category: how the window and its
/// text look, not where it sits or how it behaves.
/// </summary>
public partial class PrintPreviewDialog
{
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new FormBorderStyle FormBorderStyle
    {
        get => base.FormBorderStyle;
        set => base.FormBorderStyle = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool MaximizeBox
    {
        get => base.MaximizeBox;
        set => base.MaximizeBox = value;
    }

    [Browsable(false)]
    [DefaultValue(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool MinimizeBox
    {
        get => base.MinimizeBox;
        set => base.MinimizeBox = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public new double Opacity
    {
        get => base.Opacity;
        set => base.Opacity = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override RightToLeft RightToLeft
    {
        get => base.RightToLeft;
        set => base.RightToLeft = value;
    }

    [Browsable(false)]
    [DefaultValue(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool ShowInTaskbar
    {
        get => base.ShowInTaskbar;
        set => base.ShowInTaskbar = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool TopMost
    {
        get => base.TopMost;
        set => base.TopMost = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool UseWaitCursor
    {
        get => base.UseWaitCursor;
        set => base.UseWaitCursor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new FormWindowState WindowState
    {
        get => base.WindowState;
        set => base.WindowState = value;
    }
}
