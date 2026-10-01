using System.Drawing;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// Draws what controls do not draw themselves: frames, scroll bars, and the
/// parts of standard controls. A skin changes colours and shapes, never the
/// sizes WinForms lays out with (border widths, scroll bar width), or forms
/// would lay out differently from Windows.
/// </summary>
internal abstract class Skin
{
    /// <summary>The skin in use; the classic Windows 2000 look unless the application chose another.</summary>
    public static Skin Current { get; set; } = new ClassicSkin();

    /// <summary>Draws the frame the window styles describe, in the control's outer coordinates.</summary>
    public abstract void DrawNonClientFrame(Graphics graphics, Size outerSize, int style, int exStyle, WindowScrollInfo? horizontal, WindowScrollInfo? vertical);
}
