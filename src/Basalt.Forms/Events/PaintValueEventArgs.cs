using System.ComponentModel;

namespace System.Drawing.Design;

public class PaintValueEventArgs : EventArgs
{
    public PaintValueEventArgs(ITypeDescriptorContext? context, object? value, Graphics graphics, Rectangle bounds)
    {
        Context = context;
        Value = value;
        Graphics = graphics;
        Bounds = bounds;
    }

    public Rectangle Bounds { get; }

    public ITypeDescriptorContext? Context { get; }

    public Graphics Graphics { get; }

    public object? Value { get; }
}
