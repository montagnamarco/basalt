namespace System.Drawing.Drawing2D;

/// <summary>
/// A custom falloff for a gradient brush: Factors[i] is the blend fraction at
/// Positions[i] (0 at the start colour, 1 at the end colour), with GDI+
/// interpolating linearly between the points given.
/// </summary>
public sealed class Blend
{
    private float[] _factors;
    private float[] _positions;

    public Blend()
        : this(1)
    {
    }

    public Blend(int count)
    {
        _factors = new float[count];
        _positions = new float[count];
    }

    public float[] Factors
    {
        get => _factors;
        set => _factors = value ?? throw new ArgumentNullException(nameof(value));
    }

    public float[] Positions
    {
        get => _positions;
        set => _positions = value ?? throw new ArgumentNullException(nameof(value));
    }
}
