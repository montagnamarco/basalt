using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members a form inherits but hides from the designer, re-declared with
/// <c>new</c> so a form shows them under its own category (or not at all)
/// and forwards straight to the base implementation: the state is still
/// Control's, ContainerControl's or ScrollableControl's. Pattern:
/// <see cref="PrintPreviewDialog"/>'s own hidden members.
/// </summary>
public partial class Form
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Point Location
    {
        get => base.Location;
        set => base.Location = value;
    }

    public new Size Size
    {
        get => base.Size;
        set => base.Size = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Size ClientSize
    {
        get => base.ClientSize;
        set => base.ClientSize = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new int TabIndex
    {
        get => base.TabIndex;
        set => base.TabIndex = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Localizable(true)]
    public new Padding Margin
    {
        get => base.Margin;
        set => base.Margin = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TabIndexChanged
    {
        add => base.TabIndexChanged += value;
        remove => base.TabIndexChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TabStopChanged
    {
        add => base.TabStopChanged += value;
        remove => base.TabStopChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? AutoValidateChanged
    {
        add => base.AutoValidateChanged += value;
        remove => base.AutoValidateChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MarginChanged
    {
        add => base.MarginChanged += value;
        remove => base.MarginChanged -= value;
    }
}
