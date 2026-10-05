namespace System.Windows.Forms.VisualStyles;

/// <summary>
/// Draws one part of a visual style theme. Basalt always draws the classic
/// Windows 2000 look (<see cref="IsSupported"/> is always false), so this
/// type only reproduces the documented failure mode: constructing a renderer
/// without an active theme throws, exactly as WinForms does when visual
/// styles are unsupported or the application opted out of them. The instance
/// members preserve the public contract and reject drawing while no theme
/// backend is active, using the same failure as construction.
/// </summary>
public sealed partial class VisualStyleRenderer
{
    public VisualStyleRenderer(VisualStyleElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        throw NotSupported();
    }

    public VisualStyleRenderer(string className, int part, int state)
    {
        ArgumentNullException.ThrowIfNull(className);

        throw NotSupported();
    }

    /// <summary>Always false: Basalt never loads a visual style, so no renderer can be created.</summary>
    public static bool IsSupported => false;

    /// <summary>Always false: with no visual style loaded, no element is ever defined by one.</summary>
    public static bool IsElementDefined(VisualStyleElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return false;
    }

    private static InvalidOperationException NotSupported() =>
        new("Visual styles are not enabled on the current operating system, or the application has not enabled them.");
}
