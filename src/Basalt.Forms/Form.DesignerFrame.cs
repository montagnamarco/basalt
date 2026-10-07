using System.Drawing;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// The window frame a design surface paints around this form while it is
/// being edited: the border and title bar Visual Studio and VB6 show for the
/// form under design, carrying this form's own icon, text and control box,
/// in the same skin a non-top-level form paints its own frame with at run
/// time (see Form.Child.cs's NonClientCaption).
/// </summary>
/// <remarks>
/// A design surface hands this form's preview object to
/// <see cref="Internal.Hosting.FormsCanvas"/> with its frame included even
/// though the form is still <see cref="Control.GetTopLevel"/>: at design time
/// it never shows a window of its own (nothing ever calls Show or
/// Application.Run on it), so nothing else would ever draw its chrome.
/// NonClientCaption stays null for a genuinely top-level form on purpose - a
/// real running form's title bar belongs to the native window - so this
/// builds the caption directly from the form's own properties instead of
/// going through that guard.
/// </remarks>
public partial class Form
{
    internal void ComposeDesignerFrame(Graphics graphics)
    {
        var parameters = CreateParams;

        var caption = new WindowCaption(Text, Active: true, ControlBox, MinimizeBox, MaximizeBox,
            Maximized: false, Minimized: false, CaptionHitArea.None, ShowIcon ? Icon : null,
            FormBorderColor, FormCaptionBackColor, FormCaptionTextColor);

        Skin.Current.DrawNonClientFrame(graphics, Size, parameters.Style, parameters.ExStyle,
            GetWindowScroll(vertical: false), GetWindowScroll(vertical: true), caption);
    }
}
