using System.Text;

namespace System.Windows.Forms;

/// <summary>
/// What a control asks of its window: class, styles, position. No Win32
/// window is created from it here, but the styles still decide the frame,
/// the client area and the scroll bars, so overrides keep working.
/// </summary>
public class CreateParams
{
    public string? ClassName { get; set; }

    public string? Caption { get; set; }

    public int Style { get; set; }

    public int ExStyle { get; set; }

    public int ClassStyle { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public IntPtr Parent { get; set; }

    public object? Param { get; set; }

    public override string ToString()
    {
        var text = new StringBuilder(64);
        text.Append("CreateParams {'");
        text.Append(ClassName);
        text.Append("', '");
        text.Append(Caption);
        text.Append($"', 0x{Style:x}, 0x{ExStyle:x}, {{{X}, {Y}, {Width}, {Height}}}");
        text.Append('}');

        return text.ToString();
    }
}
