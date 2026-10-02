using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The appearance and ambient properties that a control host forwards to its
/// hosted <see cref="Control"/> instead of keeping its own value.
/// </summary>
public partial class ToolStripControlHost
{
    public override Color BackColor
    {
        get => Control.BackColor;
        set
        {
            if (value == Control.BackColor) return;

            Control.BackColor = value;
            OnBackColorChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool CausesValidation
    {
        get => Control.CausesValidation;
        set => Control.CausesValidation = value;
    }

    public override bool Enabled
    {
        get => Control.Enabled;
        set
        {
            if (value == Control.Enabled) return;

            Control.Enabled = value;
            OnEnabledChanged(EventArgs.Empty);
        }
    }

    public override Font Font
    {
        get => Control.Font;
        set
        {
            if (Equals(value, Control.Font)) return;

            Control.Font = value;
            OnFontChanged(EventArgs.Empty);
        }
    }

    public override Color ForeColor
    {
        get => Control.ForeColor;
        set
        {
            if (value == Control.ForeColor) return;

            Control.ForeColor = value;
            OnForeColorChanged(EventArgs.Empty);
        }
    }

    /// <summary>A control host paints no image of its own: the hosted Control fills the whole item.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override Image? Image
    {
        get => null;
        set
        {
            // Setting an image on a control host is a no-op, as on Microsoft's: there is nowhere to paint it.
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public override ISite? Site
    {
        get => base.Site;
        set
        {
            base.Site = value;
            Control.Site = value;
        }
    }

    [DefaultValue("")]
    public override string? Text
    {
        get => Control.Text;
        set
        {
            value ??= string.Empty;
            if (value == Control.Text) return;

            Control.Text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }
}
