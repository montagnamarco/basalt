using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Interop;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Drawing.Text;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A typeface at a size and in a style: the font text is drawn with.</summary>
/// <remarks>
/// As in GDI+, a family name that cannot be found does not fail: the font
/// falls back to Microsoft Sans Serif and remembers the name it was given in
/// OriginalFontName. Heights follow GDI+: the family's line spacing in design
/// units, scaled from its em height to the font's size.
/// </remarks>
[TypeConverter(typeof(FontConverter))]
public sealed class Font : MarshalByRefObject, ICloneable, IDisposable, ISerializable
{
    // The screen resolution GDI+ assumes for GetHeight() and Height.
    private const float ScreenDpi = 96f;

    public Font(Font prototype, FontStyle newStyle)
        : this((prototype ?? throw new ArgumentNullException(nameof(prototype))).FontFamily, prototype.Size, newStyle, prototype.Unit, prototype.GdiCharSet, prototype.GdiVerticalFont)
    {
        OriginalFontName = prototype.OriginalFontName;
    }

    public Font(FontFamily family, float emSize)
        : this(family, emSize, FontStyle.Regular, GraphicsUnit.Point)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style)
        : this(family, emSize, style, GraphicsUnit.Point)
    {
    }

    public Font(FontFamily family, float emSize, GraphicsUnit unit)
        : this(family, emSize, FontStyle.Regular, unit)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit)
        : this(family, emSize, style, unit, 1, false)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet)
        : this(family, emSize, style, unit, gdiCharSet, false)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet, bool gdiVerticalFont)
    {
        ArgumentNullException.ThrowIfNull(family);

        CheckSize(emSize);
        CheckUnit(unit);

        FontFamily = family;
        Size = emSize;
        Style = style;
        Unit = unit;
        GdiCharSet = gdiCharSet;
        GdiVerticalFont = gdiVerticalFont;
    }

    public Font(string familyName, float emSize)
        : this(familyName, emSize, FontStyle.Regular, GraphicsUnit.Point)
    {
    }

    public Font(string familyName, float emSize, FontStyle style)
        : this(familyName, emSize, style, GraphicsUnit.Point)
    {
    }

    public Font(string familyName, float emSize, GraphicsUnit unit)
        : this(familyName, emSize, FontStyle.Regular, unit)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit)
        : this(familyName, emSize, style, unit, 1, false)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet)
        : this(familyName, emSize, style, unit, gdiCharSet, false)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet, bool gdiVerticalFont)
        : this(FamilyOrFallback(familyName), emSize, style, unit, gdiCharSet, gdiVerticalFont)
    {
        OriginalFontName = familyName;
    }

    private Font(SerializationInfo info, StreamingContext context)
        : this(
            info.GetString("Name") ?? FontResolver.SansSerif,
            info.GetSingle("Size"),
            (FontStyle)info.GetValue("Style", typeof(FontStyle))!,
            (GraphicsUnit)info.GetValue("Unit", typeof(GraphicsUnit))!)
    {
    }

    ~Font() => Dispose(false);

    public FontFamily FontFamily { get; }

    public float Size { get; }

    public FontStyle Style { get; }

    public GraphicsUnit Unit { get; }

    public byte GdiCharSet { get; }

    public bool GdiVerticalFont { get; }

    public string Name => FontFamily.Name;

    public string? OriginalFontName { get; private set; }

    public bool Bold => (Style & FontStyle.Bold) != 0;

    public bool Italic => (Style & FontStyle.Italic) != 0;

    public bool Underline => (Style & FontStyle.Underline) != 0;

    public bool Strikeout => (Style & FontStyle.Strikeout) != 0;

    public bool IsSystemFont => SystemFontName.Length > 0;

    public string SystemFontName { get; private set; } = string.Empty;

    public float SizeInPoints => Unit switch
    {
        GraphicsUnit.Point => Size,
        GraphicsUnit.Inch => Size * 72f,
        GraphicsUnit.Millimeter => Size * 72f / 25.4f,
        GraphicsUnit.Document => Size * 72f / 300f,

        // Pixels and world units at the screen's resolution.
        _ => Size * 72f / ScreenDpi,
    };

    /// <summary>The line spacing in whole pixels on the screen, rounded up as GDI+ does.</summary>
    public int Height => (int)MathF.Ceiling(GetHeight());

    public float GetHeight() => GetHeight(ScreenDpi);

    /// <summary>The line spacing in pixels at a resolution.</summary>
    public float GetHeight(float dpi)
    {
        var (em, _, _, lineSpacing) = FontResolver.DesignMetrics(FontFamily.Typeface(Style));

        return lineSpacing * EmSizeInPixels(dpi) / em;
    }

    /// <summary>The line spacing in the units of the graphics' page.</summary>
    public float GetHeight(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        return GetHeight(graphics.DpiY) / graphics.PageUnitsToPixels;
    }

    public static Font FromHfont(IntPtr hfont) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Font FromHdc(IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr ToHfont() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Font FromLogFont(in LOGFONT logFont)
    {
        var face = new string(logFont.lfFaceName).TrimEnd('\0');
        var style = (logFont.lfWeight >= 600 ? FontStyle.Bold : 0)
            | (logFont.lfItalic != 0 ? FontStyle.Italic : 0)
            | (logFont.lfUnderline != 0 ? FontStyle.Underline : 0)
            | (logFont.lfStrikeOut != 0 ? FontStyle.Strikeout : 0);

        // A negative height is the em height in pixels; a positive one is the
        // cell height, ascent plus descent, from which the em follows by the
        // face's own proportions. The font comes back in world units, as
        // GDI+'s does; neither has been compared with Windows.
        var family = FamilyOrFallback(face.TrimStart('@'));
        float emSize = logFont.lfHeight switch
        {
            < 0 => -logFont.lfHeight,
            > 0 => CellToEm(family, style, logFont.lfHeight),
            _ => 16f,
        };

        return new Font(family, emSize, style, GraphicsUnit.World, logFont.lfCharSet, face.StartsWith('@')) { OriginalFontName = face };
    }

    public static Font FromLogFont(in LOGFONT logFont, IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>Reads a LOGFONT from an object laid out like one, as GDI+'s marshalling does.</summary>
    public static Font FromLogFont(object lf)
    {
        ArgumentNullException.ThrowIfNull(lf);

        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<LOGFONT>());
        try
        {
            Marshal.StructureToPtr(lf, buffer, fDeleteOld: false);

            return FromLogFont(Marshal.PtrToStructure<LOGFONT>(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static Font FromLogFont(object lf, IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void ToLogFont(out LOGFONT logFont) => ToLogFont(out logFont, ScreenDpi);

    public void ToLogFont(out LOGFONT logFont, Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        ToLogFont(out logFont, graphics.DpiY);
    }

    /// <summary>Writes a LOGFONT into a class laid out like one.</summary>
    public void ToLogFont(object logFont) => CopyLogFont(logFont, ScreenDpi);

    public void ToLogFont(object logFont, Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        CopyLogFont(logFont, graphics.DpiY);
    }

    public object Clone() => new Font(FontFamily, Size, Style, Unit, GdiCharSet, GdiVerticalFont)
    {
        OriginalFontName = OriginalFontName,
        SystemFontName = SystemFontName,
    };

    public override bool Equals(object? obj) =>
        obj is Font other
        && other.FontFamily.Equals(FontFamily)
        && other.Size == Size
        && other.Style == Style
        && other.Unit == Unit
        && other.GdiCharSet == GdiCharSet
        && other.GdiVerticalFont == GdiVerticalFont;

    public override int GetHashCode() => HashCode.Combine(Name.ToUpperInvariant(), Size, Style, Unit);

    public override string ToString() =>
        $"[{GetType().Name}: Name={Name}, Size={Size}, Units={(int)Unit}, GdiCharSet={GdiCharSet}, GdiVerticalFont={GdiVerticalFont}]";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }

    void ISerializable.GetObjectData(SerializationInfo si, StreamingContext context)
    {
        si.AddValue("Name", string.IsNullOrEmpty(OriginalFontName) ? Name : OriginalFontName);
        si.AddValue("Size", Size);
        si.AddValue("Style", Style);
        si.AddValue("Unit", Unit);
    }

    /// <summary>Marks a font as one of SystemFonts, as GDI+ does for those it hands out.</summary>
    internal Font AsSystemFont(string name)
    {
        SystemFontName = name;
        return this;
    }

    /// <summary>
    /// A Skia font for this one at a size in the caller's units, simulating a
    /// bold or italic face the family does not have, as GDI+ does.
    /// </summary>
    internal SKFont ToSkia(float size)
    {
        var typeface = FontFamily.Typeface(Style);
        var font = new SKFont(typeface, size)
        {
            Subpixel = true,
            Embolden = Bold && typeface.FontWeight < (int)SKFontStyleWeight.SemiBold,
            SkewX = Italic && typeface.FontSlant == SKFontStyleSlant.Upright ? -0.25f : 0f,
        };

        return font;
    }

    /// <summary>The em size in pixels at a resolution.</summary>
    internal float EmSizeInPixels(float dpi) => Unit switch
    {
        GraphicsUnit.Pixel or GraphicsUnit.World => Size,
        _ => SizeInPoints * dpi / 72f,
    };

    private void ToLogFont(out LOGFONT logFont, float dpi)
    {
        logFont = default;
        logFont.lfHeight = -(int)MathF.Round(EmSizeInPixels(dpi));
        logFont.lfWeight = Bold ? 700 : 400;
        logFont.lfItalic = Italic ? (byte)1 : (byte)0;
        logFont.lfUnderline = Underline ? (byte)1 : (byte)0;
        logFont.lfStrikeOut = Strikeout ? (byte)1 : (byte)0;
        logFont.lfCharSet = GdiCharSet;

        var face = (GdiVerticalFont ? "@" : "") + Name;
        face.AsSpan(0, Math.Min(face.Length, 31)).CopyTo(logFont.lfFaceName);
    }

    private void CopyLogFont(object target, float dpi)
    {
        ArgumentNullException.ThrowIfNull(target);

        ToLogFont(out var logFont, dpi);

        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<LOGFONT>());
        try
        {
            Marshal.StructureToPtr(logFont, buffer, fDeleteOld: false);

            // Only a class can be filled in place; a boxed struct would be a copy.
            Marshal.PtrToStructure(buffer, target);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static FontFamily FamilyOrFallback(string familyName)
    {
        ArgumentNullException.ThrowIfNull(familyName);

        try
        {
            return new FontFamily(familyName);
        }
        catch (ArgumentException)
        {
            return FontFamily.GenericSansSerif;
        }
    }

    private static float CellToEm(FontFamily family, FontStyle style, int cellHeight)
    {
        var (em, ascent, descent, _) = FontResolver.DesignMetrics(family.Typeface(style));

        return cellHeight * (float)em / (ascent + descent);
    }

    private static void CheckSize(float emSize)
    {
        if (float.IsNaN(emSize) || float.IsInfinity(emSize) || emSize <= 0)
        {
            throw new ArgumentException($"Value of '{emSize}' is not valid for 'emSize'. 'emSize' should be greater than 0 and less than or equal to System.Single.MaxValue.", nameof(emSize));
        }
    }

    private static void CheckUnit(GraphicsUnit unit)
    {
        // A font cannot be measured in the units of a display device.
        if (unit == GraphicsUnit.Display) throw new ArgumentException("Parameter is not valid.", nameof(unit));
    }
}
