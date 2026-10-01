using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The space inside or around a control, one value per side.
/// </summary>
/// <remarks>
/// Remembers whether the four sides were set together: <see cref="All"/>
/// answers -1 once one side is set on its own, even if it is set back to the
/// same value as the others. The designer relies on that to write
/// <c>New Padding(4)</c> or the four-value form, so it is kept as in
/// Microsoft's implementation.
/// </remarks>
[Serializable]
[TypeConverter(typeof(PaddingConverter))]
public struct Padding : IEquatable<Padding>
{
    private bool _all;
    private int _top;
    private int _left;
    private int _right;
    private int _bottom;

    public static readonly Padding Empty = new(0);

    public Padding(int all)
    {
        _all = true;
        _top = all;
        _left = all;
        _right = all;
        _bottom = all;
    }

    public Padding(int left, int top, int right, int bottom)
    {
        _top = top;
        _left = left;
        _right = right;
        _bottom = bottom;
        _all = _top == _left && _top == _right && _top == _bottom;
    }

    [RefreshProperties(RefreshProperties.All)]
    public int All
    {
        readonly get => _all ? _top : -1;
        set
        {
            if (_all && _top == value) return;

            _all = true;
            _top = value;
            _left = value;
            _right = value;
            _bottom = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int Bottom
    {
        readonly get => _all ? _top : _bottom;
        set
        {
            if (!_all && _bottom == value) return;

            SplitSides();
            _bottom = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int Left
    {
        readonly get => _all ? _top : _left;
        set
        {
            if (!_all && _left == value) return;

            SplitSides();
            _left = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int Right
    {
        readonly get => _all ? _top : _right;
        set
        {
            if (!_all && _right == value) return;

            SplitSides();
            _right = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int Top
    {
        readonly get => _top;
        set
        {
            if (!_all && _top == value) return;

            SplitSides();
            _top = value;
        }
    }

    [Browsable(false)]
    public readonly int Horizontal => Left + Right;

    [Browsable(false)]
    public readonly int Vertical => Top + Bottom;

    [Browsable(false)]
    public readonly Size Size => new(Horizontal, Vertical);

    public static Padding Add(Padding p1, Padding p2) => p1 + p2;

    public static Padding Subtract(Padding p1, Padding p2) => p1 - p2;

    public static Padding operator +(Padding p1, Padding p2) =>
        new(p1.Left + p2.Left, p1.Top + p2.Top, p1.Right + p2.Right, p1.Bottom + p2.Bottom);

    public static Padding operator -(Padding p1, Padding p2) =>
        new(p1.Left - p2.Left, p1.Top - p2.Top, p1.Right - p2.Right, p1.Bottom - p2.Bottom);

    public static bool operator ==(Padding p1, Padding p2) => p1.Equals(p2);

    public static bool operator !=(Padding p1, Padding p2) => !p1.Equals(p2);

    public override readonly bool Equals(object? other) => other is Padding padding && Equals(padding);

    public readonly bool Equals(Padding other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public override readonly string ToString() => $"{{Left={Left},Top={Top},Right={Right},Bottom={Bottom}}}";

    /// <summary>Before one side changes on its own: the sides stop being "all".</summary>
    private void SplitSides()
    {
        if (!_all) return;

        _all = false;
        _left = _top;
        _right = _top;
        _bottom = _top;
    }
}
