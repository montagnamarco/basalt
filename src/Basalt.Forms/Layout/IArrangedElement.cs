using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms.Layout;

/// <summary>
/// What a layout engine needs from what it arranges: controls, and the items
/// of a tool strip, which are laid out by the same engines without being
/// controls.
/// </summary>
internal interface IArrangedElement : IComponent
{
    Rectangle Bounds { get; }

    /// <summary>
    /// Moves the element. A layout passes <see cref="BoundsSpecified.None"/>,
    /// so that what the user set (and the anchoring measured from it) is not
    /// overwritten by what the layout decided.
    /// </summary>
    void SetBounds(Rectangle bounds, BoundsSpecified specified);

    Size GetPreferredSize(Size proposedSize);

    /// <summary>The area children are arranged in: the client area less padding and scrolling.</summary>
    Rectangle DisplayRectangle { get; }

    /// <summary>Whether the element takes part in layout: its own visibility, not its parent's.</summary>
    bool ParticipatesInLayout { get; }

    IArrangedElement? Container { get; }

    ArrangedElementCollection Children { get; }

    /// <summary>The anchoring, docking and sizing settings the engines read and write.</summary>
    LayoutSettingsStore LayoutStore { get; }

    void PerformLayout(IArrangedElement affectedElement, string? propertyName);
}
