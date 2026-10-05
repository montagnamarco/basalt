namespace System.Windows.Forms;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DockingAttribute : Attribute
{
    public static readonly DockingAttribute Default = new(DockingBehavior.Never);

    public DockingAttribute() : this(DockingBehavior.Never)
    {
    }

    public DockingAttribute(DockingBehavior dockingBehavior) => DockingBehavior = dockingBehavior;

    public DockingBehavior DockingBehavior { get; }

    public override bool Equals(object? obj) => obj is DockingAttribute other && DockingBehavior == other.DockingBehavior;

    public override int GetHashCode() => DockingBehavior.GetHashCode();

    public override bool IsDefaultAttribute() => Equals(Default);
}
