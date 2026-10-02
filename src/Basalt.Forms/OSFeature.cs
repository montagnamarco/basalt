namespace System.Windows.Forms;

/// <summary>
/// Features of the underlying platform WinForms used to probe for: layered
/// windows and themes, both present since Windows 2000 and so always
/// reported here, whether or not this port's own controls use them.
/// </summary>
public class OSFeature : FeatureSupport
{
    /// <summary>Marks a request about whether layered (translucent) windows exist.</summary>
    public static readonly object LayeredWindows = new();

    /// <summary>Marks a request about whether visual-styled (themed) controls exist.</summary>
    public static readonly object Themes = new();

    private static readonly Version Win2000 = new(5, 0, 0, 0);

    protected OSFeature()
    {
    }

    public static OSFeature Feature { get; } = new();

    public override Version GetVersionPresent(object feature) =>
        ReferenceEquals(feature, LayeredWindows) || ReferenceEquals(feature, Themes) ? Win2000 : null!;

    /// <summary>Every classic system parameter is one this platform always honours.</summary>
    public static bool IsPresent(SystemParameter parameter) => Enum.IsDefined(parameter);
}
