namespace System.Drawing;

/// <summary>The one <see cref="BufferedGraphicsContext"/> each thread gets without creating its own.</summary>
public static class BufferedGraphicsManager
{
    [ThreadStatic]
    private static BufferedGraphicsContext? _current;

    public static BufferedGraphicsContext Current => _current ??= new BufferedGraphicsContext();
}
