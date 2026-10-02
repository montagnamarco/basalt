using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// <see cref="Control.CreateAccessibilityInstance"/> for a strip: its
/// children, for assistive technology, are its items (not its Controls:
/// only a <see cref="ToolStripControlHost"/>'s hosted control is a real
/// Control), so <see cref="ToolStripAccessibleObject"/> answers
/// <see cref="GetChild"/>/<see cref="GetChildCount"/>/<see cref="HitTest"/>
/// from <see cref="ToolStrip.Items"/> through a small per-item wrapper
/// instead of the base class's child-Controls walk.
/// </summary>
public partial class ToolStrip
{
    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripAccessibleObject(this);

    /// <summary>What <see cref="Control.AccessibilityObject"/> answers for a <see cref="ToolStrip"/>.</summary>
    public class ToolStripAccessibleObject : Control.ControlAccessibleObject
    {
        public ToolStripAccessibleObject(ToolStrip owner)
            : base(owner)
        {
        }

        private ToolStrip? Strip => Owner as ToolStrip;

        public override AccessibleRole Role => AccessibleRole.ToolBar;

        public override AccessibleObject GetChild(int index)
        {
            if (Strip is not { } strip || index < 0 || index >= strip.Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ItemAccessibleObject(strip.Items[index]);
        }

        public override int GetChildCount() => Strip?.Items.Count ?? -1;

        public override AccessibleObject HitTest(int x, int y)
        {
            if (Strip is not { } strip) return null!;

            var client = strip.PointToClient(new Point(x, y));
            var item = strip.GetItemAt(client);

            return item is null ? null! : ItemAccessibleObject(item);
        }

        /// <summary>
        /// A minimal stand-in for the item's own accessible object:
        /// <see cref="ToolStripItem"/> (owned by another part of this wave)
        /// does not yet expose one, so this wrapper reads only what is
        /// already public on the item.
        /// </summary>
        private static AccessibleObject ItemAccessibleObject(ToolStripItem item) => new ToolStripItemAccessibleObject(item);

        private sealed class ToolStripItemAccessibleObject : AccessibleObject
        {
            private readonly ToolStripItem _item;

            public ToolStripItemAccessibleObject(ToolStripItem item) => _item = item;

            public override string? Name
            {
                get => _item.Text;
                set { }
            }

            public override AccessibleRole Role => AccessibleRole.PushButton;

            public override Rectangle Bounds => _item.Owner is { IsHandleCreated: true } owner
                ? owner.RectangleToScreen(_item.Bounds)
                : Rectangle.Empty;

            public override AccessibleStates State
            {
                get
                {
                    var state = AccessibleStates.None;
                    if (!_item.Enabled) state |= AccessibleStates.Unavailable;
                    if (!_item.Available) state |= AccessibleStates.Invisible;
                    if (_item.Selected) state |= AccessibleStates.Focused;

                    return state;
                }
            }

            public override AccessibleObject? Parent => _item.Owner?.AccessibilityObject;
        }
    }
}
