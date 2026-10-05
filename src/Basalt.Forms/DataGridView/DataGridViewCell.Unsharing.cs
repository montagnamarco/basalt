namespace System.Windows.Forms;

public abstract partial class DataGridViewCell
{
    // Rows in this grid are normally unique; SharedRow returns the same indexed row.
    // A future shared-row owner has Index -1, so transient per-cell state needs a
    // distinct row only in that case. The predicates below report that actual need.
    internal bool HasSharedOwningRow => DataGridView is not null && OwningRow is { Index: < 0 };
}
