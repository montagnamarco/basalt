using System.Drawing;

namespace System.Windows.Forms;

/// <summary>Values a host (the designer, an ActiveX container) offers to the top control it holds.</summary>
public sealed class AmbientProperties
{
    public Color BackColor { get; set; }

    public Color ForeColor { get; set; }

    public Font? Font { get; set; }
}
