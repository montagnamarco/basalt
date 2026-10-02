using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Members DomainUpDown re-declares only to keep the public shape Microsoft
/// declares on this type (Padding, hidden the same way UpDownBase hides
/// it), and the accessible objects for the control and for one item in its
/// <see cref="Items"/> list.
/// </summary>
public partial class DomainUpDown
{
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    /// <summary>One entry of a DomainUpDown's list, as a child of <see cref="DomainUpDownAccessibleObject"/>.</summary>
    public class DomainItemAccessibleObject : AccessibleObject
    {
        private readonly AccessibleObject _parent;

        public DomainItemAccessibleObject(string? name, AccessibleObject parent)
        {
            ArgumentNullException.ThrowIfNull(parent);

            Name = name;
            _parent = parent;
        }

        public override string? Name { get; set; }

        public override AccessibleObject? Parent => _parent;

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleStates State => AccessibleStates.Selectable;

        /// <summary>Overriding only the getter: AccessibleObject.Value's setter is still reachable through a base-typed reference, unlike a "new" member would leave it.</summary>
        public override string? Value => Name;
    }

    /// <summary>The control's own accessible object: its children are its <see cref="Items"/>, each a <see cref="DomainItemAccessibleObject"/>.</summary>
    public class DomainUpDownAccessibleObject : ControlAccessibleObject
    {
        public DomainUpDownAccessibleObject(DomainUpDown owner)
            : base(owner)
        {
        }

        private new DomainUpDown Owner => (DomainUpDown)base.Owner!;

        public override AccessibleRole Role => AccessibleRole.ComboBox;

        public override int GetChildCount() => Owner.Items.Count;

        public override AccessibleObject GetChild(int index)
        {
            if (index < 0 || index >= Owner.Items.Count) return null!;

            return new DomainItemAccessibleObject(Owner.Items[index]?.ToString(), this);
        }
    }
}
