namespace System.Drawing.Printing;

/// <summary>The four edges of a page a PrintDocument leaves blank, in hundredths of an inch.</summary>
public class Margins : ICloneable
{
    private int _left;
    private int _right;
    private int _top;
    private int _bottom;

    public Margins() : this(100, 100, 100, 100)
    {
    }

    public Margins(int left, int right, int top, int bottom)
    {
        CheckNotNegative(left, nameof(left));
        CheckNotNegative(right, nameof(right));
        CheckNotNegative(top, nameof(top));
        CheckNotNegative(bottom, nameof(bottom));

        _left = left;
        _right = right;
        _top = top;
        _bottom = bottom;
    }

    public int Left
    {
        get => _left;
        set { CheckNotNegative(value, nameof(value)); _left = value; }
    }

    public int Right
    {
        get => _right;
        set { CheckNotNegative(value, nameof(value)); _right = value; }
    }

    public int Top
    {
        get => _top;
        set { CheckNotNegative(value, nameof(value)); _top = value; }
    }

    public int Bottom
    {
        get => _bottom;
        set { CheckNotNegative(value, nameof(value)); _bottom = value; }
    }

    public object Clone() => new Margins(_left, _right, _top, _bottom);

    public override bool Equals(object? obj) =>
        obj is Margins other && other._left == _left && other._right == _right && other._top == _top && other._bottom == _bottom;

    public override int GetHashCode() => HashCode.Combine(_left, _right, _top, _bottom);

    public override string ToString() => $"[Margins Left={_left} Right={_right} Top={_top} Bottom={_bottom}]";

    public static bool operator ==(Margins? m1, Margins? m2) =>
        ReferenceEquals(m1, m2) || (m1 is not null && m2 is not null && m1.Equals(m2));

    public static bool operator !=(Margins? m1, Margins? m2) => !(m1 == m2);

    // GDI+ rejects a negative margin outright rather than clamping it, since a
    // negative value would mean printing outside the page.
    private static void CheckNotNegative(int value, string parameterName)
    {
        if (value < 0) throw new ArgumentException("Margins must be >= 0", parameterName);
    }
}
