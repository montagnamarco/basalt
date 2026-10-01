namespace System.Windows.Forms;

/// <summary>
/// The single-line edit an <see cref="UpDownBase"/> hosts: a plain
/// <see cref="TextBox"/> with no border of its own, since the container
/// draws the one frame around both the edit and the spin buttons.
/// </summary>
internal sealed class UpDownEdit : TextBox
{
    public UpDownEdit()
    {
        BorderStyle = BorderStyle.None;

        // AutoSize would otherwise snap Height back to one line on every
        // layout, undoing whatever height the owner gave the edit.
        AutoSize = false;
    }
}
