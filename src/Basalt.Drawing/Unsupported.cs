using SkiaSharp;

namespace System.Drawing;

/// <summary>
/// The messages of what cannot exist off Windows, in one place so that an
/// application hitting one reads the same explanation everywhere.
/// </summary>
internal static class Unsupported
{
    public const string NativeHandles =
        "Basalt.Drawing draws with SkiaSharp: there are no GDI or GDI+ handles (HDC, HBITMAP, HICON, native brush) to give or take.";

    /// <summary>The message GDI+ gives when a pen or brush of Pens, Brushes and the like is modified.</summary>
    public static string SharedObject(string kind) =>
        $"Changes cannot be made to {kind} because permissions are not valid.";
}

internal static class ColorExtensions
{
    public static SKColor ToSkia(this Color color) => new(color.R, color.G, color.B, color.A);

    public static Color ToDrawing(this SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
}
