using Avalonia.Input;

namespace System.Windows.Forms;

/// <summary>The standard cursors, mapped to the system's pointers of the same meaning.</summary>
public static class Cursors
{
    private static readonly Dictionary<string, Cursor> Cache = [];

    private static Cursor Get(StandardCursorType standard, string name)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var cursor))
            {
                cursor = new Cursor(standard, name);
                Cache[name] = cursor;
            }

            return cursor;
        }
    }

    public static Cursor AppStarting => Get(StandardCursorType.AppStarting, nameof(AppStarting));

    public static Cursor Arrow => Get(StandardCursorType.Arrow, nameof(Arrow));

    public static Cursor Cross => Get(StandardCursorType.Cross, nameof(Cross));

    public static Cursor Default => Get(StandardCursorType.Arrow, nameof(Default));

    public static Cursor IBeam => Get(StandardCursorType.Ibeam, nameof(IBeam));

    public static Cursor No => Get(StandardCursorType.No, nameof(No));

    public static Cursor SizeAll => Get(StandardCursorType.SizeAll, nameof(SizeAll));

    public static Cursor SizeNESW => Get(StandardCursorType.TopRightCorner, nameof(SizeNESW));

    public static Cursor SizeNS => Get(StandardCursorType.SizeNorthSouth, nameof(SizeNS));

    public static Cursor SizeNWSE => Get(StandardCursorType.TopLeftCorner, nameof(SizeNWSE));

    public static Cursor SizeWE => Get(StandardCursorType.SizeWestEast, nameof(SizeWE));

    public static Cursor UpArrow => Get(StandardCursorType.UpArrow, nameof(UpArrow));

    public static Cursor WaitCursor => Get(StandardCursorType.Wait, nameof(WaitCursor));

    public static Cursor Help => Get(StandardCursorType.Help, nameof(Help));

    public static Cursor HSplit => Get(StandardCursorType.SizeNorthSouth, nameof(HSplit));

    public static Cursor VSplit => Get(StandardCursorType.SizeWestEast, nameof(VSplit));

    public static Cursor NoMove2D => Get(StandardCursorType.SizeAll, nameof(NoMove2D));

    public static Cursor NoMoveHoriz => Get(StandardCursorType.SizeWestEast, nameof(NoMoveHoriz));

    public static Cursor NoMoveVert => Get(StandardCursorType.SizeNorthSouth, nameof(NoMoveVert));

    public static Cursor PanEast => Get(StandardCursorType.RightSide, nameof(PanEast));

    public static Cursor PanNE => Get(StandardCursorType.TopRightCorner, nameof(PanNE));

    public static Cursor PanNorth => Get(StandardCursorType.TopSide, nameof(PanNorth));

    public static Cursor PanNW => Get(StandardCursorType.TopLeftCorner, nameof(PanNW));

    public static Cursor PanSE => Get(StandardCursorType.BottomRightCorner, nameof(PanSE));

    public static Cursor PanSouth => Get(StandardCursorType.BottomSide, nameof(PanSouth));

    public static Cursor PanSW => Get(StandardCursorType.BottomLeftCorner, nameof(PanSW));

    public static Cursor PanWest => Get(StandardCursorType.LeftSide, nameof(PanWest));

    public static Cursor Hand => Get(StandardCursorType.Hand, nameof(Hand));
}
