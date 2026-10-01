namespace System.Windows.Forms;

/// <summary>
/// Splits a data member such as "Orders.OrderDate" into the path that
/// navigates to a related list ("Orders") and the field read on its current
/// item ("OrderDate"); a plain field has an empty path.
/// </summary>
public struct BindingMemberInfo : IEquatable<BindingMemberInfo>
{
    public BindingMemberInfo(string? dataMember)
    {
        BindingMember = dataMember ?? string.Empty;

        var lastDot = BindingMember.LastIndexOf('.');
        if (lastDot < 0)
        {
            BindingPath = string.Empty;
            BindingField = BindingMember;
        }
        else
        {
            BindingPath = BindingMember[..lastDot];
            BindingField = BindingMember[(lastDot + 1)..];
        }
    }

    public string BindingField { get; }

    public string BindingMember { get; }

    public string BindingPath { get; }

    public override bool Equals(object? otherObject) => otherObject is BindingMemberInfo other && Equals(other);

    /// <summary>The whole member decides equality: WinForms compares it case-insensitively.</summary>
    public bool Equals(BindingMemberInfo other) => string.Equals(BindingMember, other.BindingMember, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(BindingMember);

    public static bool operator ==(BindingMemberInfo a, BindingMemberInfo b) => a.Equals(b);

    public static bool operator !=(BindingMemberInfo a, BindingMemberInfo b) => !a.Equals(b);
}
