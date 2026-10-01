namespace System.Drawing.Imaging.Effects;

/// <summary>
/// The shared shape of GDI+'s eight curve adjustments: each reads one channel
/// (or <see cref="CurveChannel.All"/>) and moves it along a tonal curve the
/// concrete subclass names.
/// </summary>
public abstract class ColorCurveEffect : Effect
{
    private protected ColorCurveEffect(CurveChannel channel) => Channel = channel;

    public CurveChannel Channel { get; }
}
