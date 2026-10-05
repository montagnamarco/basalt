using Avalonia;

namespace Basalt.Shell.Controls;

/// <summary>Geometry shared by the selected controls during a group drag.</summary>
internal static class DesignerSelectionBounds
{
    public static Rect? Enclose(IEnumerable<Rect> bounds)
    {
        using var iterator = bounds.GetEnumerator();

        if (!iterator.MoveNext()) return null;

        var left = iterator.Current.Left;
        var top = iterator.Current.Top;
        var right = iterator.Current.Right;
        var bottom = iterator.Current.Bottom;

        while (iterator.MoveNext())
        {
            left = Math.Min(left, iterator.Current.Left);
            top = Math.Min(top, iterator.Current.Top);
            right = Math.Max(right, iterator.Current.Right);
            bottom = Math.Max(bottom, iterator.Current.Bottom);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
