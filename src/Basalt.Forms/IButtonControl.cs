namespace System.Windows.Forms;

/// <summary>A control that can be a form's accept or cancel button.</summary>
public interface IButtonControl
{
    DialogResult DialogResult { get; set; }

    void NotifyDefault(bool value);

    void PerformClick();
}
