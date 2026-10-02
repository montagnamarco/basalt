using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Everything an item exposes to assistive technology: the lazily built
/// <see cref="AccessibleObject"/>, the Accessible* properties a form author
/// sets by hand, and the nested object itself. An item is not a
/// <see cref="Control"/> (it has no handle to wrap), so unlike
/// <see cref="Control.ControlAccessibleObject"/> everything here is computed
/// straight from the owning item's own state.
/// </summary>
public abstract partial class ToolStripItem
{
    private AccessibleObject? _accessibilityObject;
    private string? _accessibleDefaultActionDescription;
    private string? _accessibleDescription;
    private string? _accessibleName;
    private AccessibleRole _accessibleRole = AccessibleRole.Default;

    private static readonly object s_queryAccessibilityHelpEvent = new();

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AccessibleObject AccessibilityObject => _accessibilityObject ??= CreateAccessibilityInstance();

    [DefaultValue(null)]
    [Localizable(true)]
    public string? AccessibleDefaultActionDescription
    {
        get => _accessibleDefaultActionDescription;
        set => _accessibleDefaultActionDescription = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    public string? AccessibleDescription
    {
        get => _accessibleDescription;
        set => _accessibleDescription = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    public string? AccessibleName
    {
        get => _accessibleName;
        set => _accessibleName = value;
    }

    [DefaultValue(AccessibleRole.Default)]
    public AccessibleRole AccessibleRole
    {
        get => _accessibleRole;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AccessibleRole));

            _accessibleRole = value;
        }
    }

    /// <summary>Raised when assistive technology (or AccessibleObject.Help) asks this item to describe itself.</summary>
    public event QueryAccessibilityHelpEventHandler? QueryAccessibilityHelp
    {
        add => Events.AddHandler(s_queryAccessibilityHelpEvent, value);
        remove => Events.RemoveHandler(s_queryAccessibilityHelpEvent, value);
    }

    internal void RaiseQueryAccessibilityHelp(QueryAccessibilityHelpEventArgs e) =>
        ((QueryAccessibilityHelpEventHandler?)Events[s_queryAccessibilityHelpEvent])?.Invoke(this, e);

    protected virtual AccessibleObject CreateAccessibilityInstance() => new ToolStripItemAccessibleObject(this);

    /// <summary>What <see cref="AccessibilityObject"/> answers for a plain item: a button, a label, a separator.</summary>
    public class ToolStripItemAccessibleObject : AccessibleObject
    {
        private readonly ToolStripItem _ownerItem;
        private AccessibleStates _additionalState;

        public ToolStripItemAccessibleObject(ToolStripItem ownerItem)
        {
            ArgumentNullException.ThrowIfNull(ownerItem);

            _ownerItem = ownerItem;
        }

        /// <summary>Lets a subclass (a checked button, say) add a bit State does not already compute on its own.</summary>
        public void AddState(AccessibleStates state) => _additionalState |= state;

        /// <summary>
        /// Not part of Microsoft's public contract (no such member exists there
        /// either): it only lets <see cref="ToolStripDropDownItemAccessibleObject"/>,
        /// a sibling type rather than a nested one, reach the owning item without
        /// a second, duplicate field of its own.
        /// </summary>
        internal ToolStripItem OwnerItem => _ownerItem;

        public override Rectangle Bounds =>
            _ownerItem.GetCurrentParent() is { } parent ? parent.RectangleToScreen(_ownerItem.Bounds) : Rectangle.Empty;

        public override string? DefaultAction =>
            !string.IsNullOrEmpty(_ownerItem.AccessibleDefaultActionDescription) ? _ownerItem.AccessibleDefaultActionDescription : "Click";

        public override string? Description => _ownerItem.AccessibleDescription;

        public override string? Help
        {
            get
            {
                var arguments = new QueryAccessibilityHelpEventArgs();
                _ownerItem.RaiseQueryAccessibilityHelp(arguments);
                return arguments.HelpString;
            }
        }

        /// <summary>Alt+ the mnemonic in the item's own text, the way a button's or a menu item's caption carries one.</summary>
        public override string? KeyboardShortcut
        {
            get
            {
                var mnemonic = MnemonicOf(_ownerItem.Text);
                return mnemonic is null ? null : $"Alt+{char.ToUpperInvariant(mnemonic.Value)}";
            }
        }

        /// <summary>The owner's AccessibleName, else its own Text, mnemonic marker stripped.</summary>
        public override string? Name
        {
            get
            {
                var name = !string.IsNullOrEmpty(_ownerItem.AccessibleName) ? _ownerItem.AccessibleName : _ownerItem.Text;
                return string.IsNullOrEmpty(name) ? null : StripMnemonic(name);
            }
            set => _ownerItem.AccessibleName = value;
        }

        public override AccessibleObject? Parent => _ownerItem.GetCurrentParent()?.AccessibilityObject;

        public override AccessibleRole Role =>
            _ownerItem.AccessibleRole == AccessibleRole.Default ? AccessibleRole.PushButton : _ownerItem.AccessibleRole;

        public override AccessibleStates State
        {
            get
            {
                var state = _additionalState;

                if (!_ownerItem.Enabled) state |= AccessibleStates.Unavailable;
                if (!_ownerItem.Visible) state |= AccessibleStates.Invisible;
                if (_ownerItem.CanSelect) state |= AccessibleStates.Focusable;
                if (_ownerItem.Selected) state |= AccessibleStates.Focused;
                if (_ownerItem.Pressed) state |= AccessibleStates.Pressed;

                return state;
            }
        }

        public override void DoDefaultAction() => _ownerItem.PerformClick();

        public override int GetHelpTopic(out string? fileName)
        {
            fileName = null;
            return -1;
        }

        /// <summary>An item has no accessible children or siblings of its own to navigate to.</summary>
        public override AccessibleObject Navigate(AccessibleNavigation navdir) => null!;

        public override string ToString() => $"ToolStripItemAccessibleObject: Owner = {_ownerItem}";

        private static char? MnemonicOf(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            for (var i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '&') continue;
                if (text[i + 1] == '&') { i++; continue; }

                return text[i + 1];
            }

            return null;
        }

        private static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", "").Replace('\0', '&');
    }
}
