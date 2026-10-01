namespace System.Drawing.Drawing2D;

/// <summary>A custom multi-colour gradient: Colors[i] sits at Positions[i], interpolated linearly in between.</summary>
public sealed class ColorBlend
{
    private Color[] _colors;
    private float[] _positions;

    public ColorBlend()
        : this(1)
    {
    }

    public ColorBlend(int count)
    {
        _colors = new Color[count];
        _positions = new float[count];
    }

    public Color[] Colors
    {
        get => _colors;
        set => _colors = value ?? throw new ArgumentNullException(nameof(value));
    }

    public float[] Positions
    {
        get => _positions;
        set => _positions = value ?? throw new ArgumentNullException(nameof(value));
    }
}
