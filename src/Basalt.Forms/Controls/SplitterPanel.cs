using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One side of a <see cref="SplitContainer"/>. It is a plain panel; the
/// container places it and reacts to its own events, so a SplitterPanel does
/// nothing a Panel would not.
/// </summary>
public sealed partial class SplitterPanel : Panel
{
    public SplitterPanel(SplitContainer owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Owner = owner;
    }

    /// <summary>The container this panel belongs to; not public in WinForms either.</summary>
    internal SplitContainer Owner { get; }

    /// <summary>A SplitterPanel is placed by its SplitContainer, so it carries none of the gap a plain Panel leaves for its neighbours.</summary>
    protected override Padding DefaultMargin => Padding.Empty;
}
