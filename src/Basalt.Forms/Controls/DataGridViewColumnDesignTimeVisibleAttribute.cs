namespace System.Windows.Forms;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DataGridViewColumnDesignTimeVisibleAttribute : Attribute
{
    public static readonly DataGridViewColumnDesignTimeVisibleAttribute Yes = new(true);
    public static readonly DataGridViewColumnDesignTimeVisibleAttribute No = new(false);
    public static readonly DataGridViewColumnDesignTimeVisibleAttribute Default = Yes;

    public DataGridViewColumnDesignTimeVisibleAttribute() : this(true)
    {
    }

    public DataGridViewColumnDesignTimeVisibleAttribute(bool visible) => Visible = visible;

    public bool Visible { get; }

    public override bool Equals(object? obj) => obj is DataGridViewColumnDesignTimeVisibleAttribute other && Visible == other.Visible;

    public override int GetHashCode() => Visible.GetHashCode();

    public override bool IsDefaultAttribute() => Equals(Default);
}
