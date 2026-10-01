namespace System.Windows.Forms.Layout;

/// <summary>
/// Arranges a container's children. A control asks its own engine to lay out
/// its children, and tells its parent's engine when its bounds change, so
/// the engine can remember how each child relates to the container.
/// </summary>
public abstract class LayoutEngine
{
    protected LayoutEngine()
    {
    }

    /// <summary>Called when a child is added or its bounds are set from outside a layout.</summary>
    public virtual void InitLayout(object child, BoundsSpecified specified)
    {
        AsControl(child, nameof(child));
    }

    /// <summary>Arranges the container's children; true asks the container's parent to lay out again.</summary>
    public virtual bool Layout(object container, LayoutEventArgs layoutEventArgs)
    {
        AsControl(container, nameof(container));

        return false;
    }

    private protected static Control AsControl(object value, string name) =>
        value as Control ?? throw new NotSupportedException($"LayoutEngine cannot lay out an object of type {value?.GetType()}.");
}
