using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A container for other controls, with an optional border.</summary>
[DefaultProperty("BorderStyle")]
[DefaultEvent("Paint")]
public partial class Panel : ScrollableControl
{
    private BorderStyle _borderStyle = BorderStyle.None;

    public Panel()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint, false);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        base.TabStop = false;
    }

    protected override Size DefaultSize => new(200, 100);

    [DefaultValue(BorderStyle.None)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));

            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
        }
    }

    /// <summary>The border is the window's frame: FixedSingle a plain border, Fixed3D a sunken edge.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WindowStyles.ExControlParent;

            if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;
            else if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    [DefaultValue(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    public override string ToString() => $"{base.ToString()}, BorderStyle: {_borderStyle}";
}
