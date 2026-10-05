using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A container of controls meant to be reused as one piece, with its own Load event and border.</summary>
[DesignerCategory("UserControl")]
[DefaultEvent("Load")]
public partial class UserControl : ContainerControl
{
    private BorderStyle _borderStyle = BorderStyle.None;
    private bool _loaded;

    public UserControl()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override Size DefaultSize => new(150, 150);

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    [DefaultValue(AutoSizeMode.GrowOnly)]
    [Localizable(true)]
    public AutoSizeMode AutoSizeMode
    {
        get => GetAutoSizeMode();
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoSizeMode));

            SetAutoSizeMode(value);
        }
    }

    /// <summary>The frame drawn at the control's own edge: none, a thin line, or the sunken look of a dialog field.</summary>
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

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;
            else if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Bindable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>Raised once, the first time the control's handle (and so its children's) is created while it can be shown.</summary>
    public event EventHandler? Load
    {
        add => Events.AddHandler(s_loadEvent, value);
        remove => Events.RemoveHandler(s_loadEvent, value);
    }

    private static readonly object s_loadEvent = new();

    protected virtual void OnLoad(EventArgs e) => ((EventHandler?)Events[s_loadEvent])?.Invoke(this, e);

    /// <summary>The moment WinForms raises Load: once, right after this control (and its children) first get a handle.</summary>
    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        if (_loaded) return;

        _loaded = true;
        OnLoad(EventArgs.Empty);
    }

    /// <summary>A click anywhere in the control, even on no particular child, still gives the whole piece the focus.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (CanFocus) Focus();
    }
}
