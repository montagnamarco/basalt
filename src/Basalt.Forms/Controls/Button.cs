using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A push button: clicking it raises Click and, with a DialogResult, closes its modal form with that result.</summary>
public partial class Button : ButtonBase, IButtonControl
{
    private DialogResult _dialogResult;

    public Button()
    {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
    }

    [DefaultValue(DialogResult.None)]
    public virtual DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DialogResult));

            _dialogResult = value;
        }
    }

    /// <summary>The form tells its accept button that it is the default, to draw the dark frame.</summary>
    public virtual void NotifyDefault(bool value) => IsDefault = value;

    /// <summary>Clicks the button from code, if it can be clicked.</summary>
    public void PerformClick()
    {
        if (CanSelect || (Visible && Enabled)) OnClick(EventArgs.Empty);
    }

    /// <summary>The form gets the result before the handlers run, as WinForms sets it.</summary>
    protected override void OnClick(EventArgs e)
    {
        if (FindForm() is { } form && _dialogResult != DialogResult.None) form.DialogResult = _dialogResult;

        base.OnClick(e);
    }

    /// <summary>A button clicks on mouse release over it; double clicks are two clicks.</summary>
    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        var clicked = mevent.Button == MouseButtons.Left && Pushed && ClientRectangle.Contains(mevent.Location);

        base.OnMouseUp(mevent);

        if (clicked)
        {
            OnClick(mevent);
            OnMouseClick(mevent);
        }
    }

    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (UseMnemonic && CanSelectForMnemonic() && IsMnemonic(charCode, Text))
        {
            PerformClick();
            return true;
        }

        return base.ProcessMnemonic(charCode);
    }

    public override string ToString() => $"{base.ToString()}, Text: {Text}";
}
