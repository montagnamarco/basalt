using System.ComponentModel;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// The flow settings of a container: direction, whether it wraps, and which
/// children end their row or column early. Created by the container that
/// owns the settings (<see cref="FlowLayoutPanel"/> today), never directly.
/// </summary>
/// <remarks>
/// Reads and writes the same <see cref="IArrangedElement.LayoutStore"/>
/// extended values the <see cref="FlowLayout"/> engine itself reads, so a
/// panel's own properties and its settings object never disagree.
/// </remarks>
[DefaultProperty("FlowDirection")]
public class FlowLayoutSettings : LayoutSettings
{
    private readonly IArrangedElement _owner;

    internal FlowLayoutSettings(IArrangedElement owner)
    {
        _owner = owner;
    }

    [DefaultValue(FlowDirection.LeftToRight)]
    public FlowDirection FlowDirection
    {
        get => FlowLayout.GetFlowDirection(_owner);
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlowDirection));

            FlowLayout.SetFlowDirection(_owner, value);
        }
    }

    public override LayoutEngine LayoutEngine => FlowLayout.Instance;

    [DefaultValue(true)]
    public bool WrapContents
    {
        get => FlowLayout.GetWrapContents(_owner);
        set => FlowLayout.SetWrapContents(_owner, value);
    }

    public bool GetFlowBreak(object child) => FlowLayout.GetFlowBreak(AsElement(child));

    public void SetFlowBreak(object child, bool value) => FlowLayout.SetFlowBreak(AsElement(child), value);

    private static IArrangedElement AsElement(object child) =>
        child as IArrangedElement ?? throw new ArgumentException("The child must be a control.", nameof(child));
}
