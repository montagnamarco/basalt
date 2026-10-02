using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The Form members a print preview dialog hides from the designer that
/// Microsoft files under scaling, sizing and the window's chrome: now that
/// <see cref="Form"/> itself carries them (AutoScale, HelpButton, Icon,
/// RightToLeftLayout, SizeGripStyle, TransparencyKey and the minimum/maximum
/// size change events), this dialog hides them the same way it hides the
/// appearance ones in PrintPreviewDialog.HiddenAppearance.cs.
/// </summary>
public partial class PrintPreviewDialog
{
    [Obsolete("This property has been deprecated. Use the AutoScaleMode property instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool AutoScale
    {
        get => base.AutoScale;
        set => base.AutoScale = value;
    }

    [Obsolete("This property has been deprecated. Use the AutoScaleDimensions property instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override Size AutoScaleBaseSize
    {
        get => base.AutoScaleBaseSize;
        set => base.AutoScaleBaseSize = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool HelpButton
    {
        get => base.HelpButton;
        set => base.HelpButton = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Icon? Icon
    {
        get => base.Icon;
        set => base.Icon = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool RightToLeftLayout
    {
        get => base.RightToLeftLayout;
        set => base.RightToLeftLayout = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new SizeGripStyle SizeGripStyle
    {
        get => base.SizeGripStyle;
        set => base.SizeGripStyle = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Color TransparencyKey
    {
        get => base.TransparencyKey;
        set => base.TransparencyKey = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MaximumSizeChanged
    {
        add => base.MaximumSizeChanged += value;
        remove => base.MaximumSizeChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MinimumSizeChanged
    {
        add => base.MinimumSizeChanged += value;
        remove => base.MinimumSizeChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? RightToLeftLayoutChanged
    {
        add => base.RightToLeftLayoutChanged += value;
        remove => base.RightToLeftLayoutChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new DockPaddingEdges DockPadding => base.DockPadding;
}
