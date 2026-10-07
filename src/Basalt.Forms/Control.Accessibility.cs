using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Everything a control exposes to assistive technology: the lazily built
/// <see cref="AccessibleObject"/>, the Accessible* properties a form author
/// sets by hand, and the QueryAccessibilityHelp event a HelpProvider answers
/// through.
/// </summary>
public partial class Control
{
    private AccessibleObject? _accessibilityObject;
    private string? _accessibleDefaultActionDescription;
    private string? _accessibleDescription;
    private string? _accessibleName;
    private AccessibleRole _accessibleRole = AccessibleRole.Default;
    private bool _isAccessible = true;

    private static readonly object s_queryAccessibilityHelpEvent = new();

    /// <summary>The object built once, the first time it is asked for, and kept for the control's life.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AccessibleObject AccessibilityObject => _accessibilityObject ??= CreateAccessibilityInstance();

    [DefaultValue(null)]
    [Localizable(true)]
    [Category("Accessibility")]
    public string? AccessibleDefaultActionDescription
    {
        get => _accessibleDefaultActionDescription;
        set => _accessibleDefaultActionDescription = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    [Category("Accessibility")]
    public string? AccessibleDescription
    {
        get => _accessibleDescription;
        set => _accessibleDescription = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    [Category("Accessibility")]
    public string? AccessibleName
    {
        get => _accessibleName;
        set => _accessibleName = value;
    }

    [DefaultValue(AccessibleRole.Default)]
    [Category("Accessibility")]
    public AccessibleRole AccessibleRole
    {
        get => _accessibleRole;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AccessibleRole));

            _accessibleRole = value;
        }
    }

    [DefaultValue(true)]
    [Category("Accessibility")]
    public bool IsAccessible
    {
        get => _isAccessible;
        set => _isAccessible = value;
    }

    /// <summary>Raised when assistive technology (or AccessibleObject.Help) asks this control to describe itself.</summary>
    [Category("Behavior")]
    public event QueryAccessibilityHelpEventHandler? QueryAccessibilityHelp
    {
        add => Events.AddHandler(s_queryAccessibilityHelpEvent, value);
        remove => Events.RemoveHandler(s_queryAccessibilityHelpEvent, value);
    }

    internal void RaiseQueryAccessibilityHelp(QueryAccessibilityHelpEventArgs e) =>
        ((QueryAccessibilityHelpEventHandler?)Events[s_queryAccessibilityHelpEvent])?.Invoke(this, e);

    /// <summary>
    /// Tells listening clients that something changed. There is no platform
    /// accessibility bridge yet (no screen reader is actually listening), so
    /// this stays a documented no-op: callable, so callers and overrides do
    /// not need to special-case us while one is being built.
    /// </summary>
    protected void AccessibilityNotifyClients(AccessibleEvents accEvent, int childID)
    {
    }

    protected void AccessibilityNotifyClients(AccessibleEvents accEvent, int objectID, int childID)
    {
    }

    protected virtual AccessibleObject CreateAccessibilityInstance() => new ControlAccessibleObject(this);

    /// <summary>What <see cref="AccessibilityObject"/> answers for an ordinary control.</summary>
    public class ControlAccessibleObject : AccessibleObject
    {
        private IntPtr _handle;

        public ControlAccessibleObject(Control ownerControl)
        {
            ArgumentNullException.ThrowIfNull(ownerControl);

            Owner = ownerControl;
        }

        internal override Control? OwnerControl => Owner;

        public Control? Owner { get; }

        public override string? DefaultAction => Owner?.AccessibleDefaultActionDescription;

        public override string? Description => Owner?.AccessibleDescription;

        /// <summary>Set by a caller that already knows the native window; otherwise the owner's own handle, if it has one.</summary>
        public IntPtr Handle
        {
            get => _handle != IntPtr.Zero ? _handle : Owner is { IsHandleCreated: true } owner ? owner.Handle : IntPtr.Zero;
            set => _handle = value;
        }

        public override string? Help
        {
            get
            {
                if (Owner is not { } owner) return base.Help;

                var arguments = new QueryAccessibilityHelpEventArgs();
                owner.RaiseQueryAccessibilityHelp(arguments);
                return arguments.HelpString;
            }
        }

        /// <summary>Alt+ the mnemonic in the owner's own text, or failing that, in the label just before it in tab order.</summary>
        public override string? KeyboardShortcut
        {
            get
            {
                if (Owner is not { } owner) return base.KeyboardShortcut;

                var mnemonic = MnemonicOf(LabelTextOf(owner));
                return mnemonic is null ? null : $"Alt+{char.ToUpperInvariant(mnemonic.Value)}";
            }
        }

        /// <summary>The owner's AccessibleName, else the text that labels it on screen (<see cref="LabelTextOf"/>), mnemonic stripped.</summary>
        public override string? Name
        {
            get
            {
                if (Owner is not { } owner) return base.Name;

                var name = !string.IsNullOrEmpty(owner.AccessibleName) ? owner.AccessibleName : LabelTextOf(owner);
                return name is null ? null : StripMnemonic(name);
            }
            set
            {
                if (Owner is { } owner) owner.AccessibleName = value;
            }
        }

        public override AccessibleObject? Parent => Owner?.Parent?.AccessibilityObject;

        public override AccessibleRole Role
        {
            get
            {
                if (Owner is not { } owner) return base.Role;

                return owner.AccessibleRole == AccessibleRole.Default ? AccessibleRole.Client : owner.AccessibleRole;
            }
        }

        public override int GetHelpTopic(out string? fileName)
        {
            fileName = null;
            return -1;
        }

        public void NotifyClients(AccessibleEvents accEvent) => Owner?.AccessibilityNotifyClients(accEvent, 0);

        public void NotifyClients(AccessibleEvents accEvent, int childID) => Owner?.AccessibilityNotifyClients(accEvent, childID);

        public void NotifyClients(AccessibleEvents accEvent, int objectID, int childID) => Owner?.AccessibilityNotifyClients(accEvent, objectID, childID);

        public override bool RaiseLiveRegionChanged() => false;

        public override string ToString() => $"ControlAccessibleObject: Owner = {Owner}";

        /// <summary>The sibling immediately before the owner in tab order, when it is a Label.</summary>
        /// <summary>
        /// The text that labels a control on screen, as Windows' accessibility
        /// proxies read it: a control that shows its own caption (a button, a
        /// check box, a label, a group box, a tab page, a form's title) is named by that
        /// caption; any other control (a text box, a list) by the label just
        /// before it in tab order.
        /// </summary>
        private static string? LabelTextOf(Control owner) =>
            owner is ButtonBase or Label or GroupBox or TabPage or Form ? owner.Text : PrecedingLabel(owner)?.Text;

        private static Label? PrecedingLabel(Control owner)
        {
            if (owner.Parent is not { } parent) return null;

            var siblings = parent.ChildrenInTabOrder();
            var index = Array.IndexOf(siblings, owner);

            return index > 0 ? siblings[index - 1] as Label : null;
        }

        /// <summary>The character right after a single, unescaped '&amp;' - the one Alt+key underlines.</summary>
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

        /// <summary>Removes the mnemonic marker, keeping a doubled "&amp;&amp;" as the single literal ampersand it stands for.</summary>
        private static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", "").Replace('\0', '&');
    }
}
