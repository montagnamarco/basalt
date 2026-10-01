namespace Microsoft.VisualBasic.MyServices.Internal;

/// <summary>A plain value holder, as Microsoft's internal plumbing for My.* instance caching uses.</summary>
public class ContextValue<T>
{
    public T? Value { get; set; }
}
