using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// How an image is recoloured while it is drawn: GDI+ keeps a separate set of
/// adjustments for each <see cref="ColorAdjustType"/> (the image as a whole,
/// or just its use as a bitmap, brush, pen or text). A category with nothing
/// of its own set uses <see cref="ColorAdjustType.Default"/>'s; one with any
/// setting of its own uses only its own. <see cref="ToColorFilter"/> turns
/// the result into the SkiaSharp filter the DrawImage overloads apply.
/// </summary>
public sealed class ImageAttributes : ICloneable, IDisposable
{
    private readonly Adjustments?[] _byType = new Adjustments?[5];

    private WrapMode _wrapMode = WrapMode.Clamp;
    private Color _wrapColor;
    private bool _wrapClamp;

    public ImageAttributes()
    {
    }

    private ImageAttributes(ImageAttributes source)
    {
        for (var i = 0; i < _byType.Length; i++) _byType[i] = source._byType[i]?.Clone();

        _wrapMode = source._wrapMode;
        _wrapColor = source._wrapColor;
        _wrapClamp = source._wrapClamp;
    }

    ~ImageAttributes() => Dispose(false);

    public void ClearBrushRemapTable() => ClearRemapTable(ColorAdjustType.Brush);

    public void ClearColorKey() => ClearColorKey(ColorAdjustType.Default);

    public void ClearColorKey(ColorAdjustType type) => Clear(type, settings => settings.ColorKey = null);

    public void ClearColorMatrix() => ClearColorMatrix(ColorAdjustType.Default);

    public void ClearColorMatrix(ColorAdjustType type) => Clear(type, settings => settings.ColorMatrix = null);

    public void ClearGamma() => ClearGamma(ColorAdjustType.Default);

    public void ClearGamma(ColorAdjustType type) => Clear(type, settings => settings.Gamma = null);

    public void ClearNoOp() => ClearNoOp(ColorAdjustType.Default);

    public void ClearNoOp(ColorAdjustType type) => Clear(type, settings => settings.NoOp = false);

    // Output channel separation (CMYK) was never implemented (see SetOutputChannel),
    // so nothing is ever stored for it; clearing what was never set is a no-op,
    // exactly as GDI+ would see it.
    public void ClearOutputChannel()
    {
    }

    public void ClearOutputChannel(ColorAdjustType type) => RequireCategory(type);

    public void ClearOutputChannelColorProfile()
    {
    }

    public void ClearOutputChannelColorProfile(ColorAdjustType type) => RequireCategory(type);

    public void ClearRemapTable() => ClearRemapTable(ColorAdjustType.Default);

    public void ClearRemapTable(ColorAdjustType type) => Clear(type, settings => settings.RemapTable = null);

    public void ClearThreshold() => ClearThreshold(ColorAdjustType.Default);

    public void ClearThreshold(ColorAdjustType type) => Clear(type, settings => settings.Threshold = null);

    public object Clone() => new ImageAttributes(this);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Mutates every entry of <paramref name="palette"/> the way <see cref="ToColorFilter"/> would recolour that same colour drawn as a pixel.</summary>
    public void GetAdjustedPalette(ColorPalette palette, ColorAdjustType type)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var effective = Resolve(type);
        if (effective is null) return;

        var entries = palette.Entries;
        for (var i = 0; i < entries.Length; i++) entries[i] = effective.Value.Apply(entries[i]);
    }

    public void SetBrushRemapTable(ColorMap[] map) => SetRemapTable(map, ColorAdjustType.Brush);

    public void SetBrushRemapTable(ReadOnlySpan<ColorMap> map) => SetRemapTable(ColorAdjustType.Brush, map);

    public void SetBrushRemapTable(ReadOnlySpan<(Color OldColor, Color NewColor)> map) => SetRemapTable(ColorAdjustType.Brush, map);

    public void SetColorKey(Color colorLow, Color colorHigh) => SetColorKey(colorLow, colorHigh, ColorAdjustType.Default);

    public void SetColorKey(Color colorLow, Color colorHigh, ColorAdjustType type) =>
        AdjustmentsOf(type).ColorKey = (colorLow, colorHigh);

    public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix grayMatrix) =>
        SetColorMatrices(newColorMatrix, grayMatrix, ColorMatrixFlag.Default, ColorAdjustType.Default);

    public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix grayMatrix, ColorMatrixFlag flags) =>
        SetColorMatrices(newColorMatrix, grayMatrix, flags, ColorAdjustType.Default);

    // grayMatrix only matters for ColorMatrixFlag.AltGrays, which is not
    // implemented (see SetColorMatrix below): it is validated but never kept.
    public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix grayMatrix, ColorMatrixFlag flags, ColorAdjustType type)
    {
        ArgumentNullException.ThrowIfNull(grayMatrix);

        SetColorMatrix(newColorMatrix, flags, type);
    }

    public void SetColorMatrix(ColorMatrix newColorMatrix) => SetColorMatrix(newColorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Default);

    public void SetColorMatrix(ColorMatrix newColorMatrix, ColorMatrixFlag flags) =>
        SetColorMatrix(newColorMatrix, flags, ColorAdjustType.Default);

    public void SetColorMatrix(ColorMatrix newColorMatrix, ColorMatrixFlag flags, ColorAdjustType type)
    {
        ArgumentNullException.ThrowIfNull(newColorMatrix);

        var adjustments = AdjustmentsOf(type);
        adjustments.ColorMatrix = newColorMatrix;
        adjustments.ColorMatrixFlag = flags;
    }

    public void SetGamma(float gamma) => SetGamma(gamma, ColorAdjustType.Default);

    public void SetGamma(float gamma, ColorAdjustType type) => AdjustmentsOf(type).Gamma = gamma;

    public void SetNoOp() => SetNoOp(ColorAdjustType.Default);

    public void SetNoOp(ColorAdjustType type) => AdjustmentsOf(type).NoOp = true;

    /// <summary>
    /// Separating an image into one of its CMYK channels needs a CMYK
    /// decoder this library does not have (see docs/planning): there is no
    /// approximation that would not silently show the wrong channel.
    /// </summary>
    public void SetOutputChannel(ColorChannelFlag flags) => SetOutputChannel(flags, ColorAdjustType.Default);

    public void SetOutputChannel(ColorChannelFlag flags, ColorAdjustType type)
    {
        RequireCategory(type);
        throw new NotImplementedException("SetOutputChannel (splitting an image into one of its CMYK channels) is not implemented yet.");
    }

    public void SetOutputChannelColorProfile(string colorProfileFilename) =>
        SetOutputChannelColorProfile(colorProfileFilename, ColorAdjustType.Default);

    public void SetOutputChannelColorProfile(string colorProfileFilename, ColorAdjustType type)
    {
        ArgumentNullException.ThrowIfNull(colorProfileFilename);
        RequireCategory(type);
        throw new NotImplementedException(
            "SetOutputChannelColorProfile is not implemented yet: it only matters once SetOutputChannel (CMYK channels) is.");
    }

    public void SetRemapTable(ColorMap[] map) => SetRemapTable(map, ColorAdjustType.Default);

    public void SetRemapTable(ColorMap[] map, ColorAdjustType type)
    {
        ArgumentNullException.ThrowIfNull(map);

        AdjustmentsOf(type).RemapTable = Copy(map);
    }

    public void SetRemapTable(ReadOnlySpan<ColorMap> map) => SetRemapTable(ColorAdjustType.Default, map);

    public void SetRemapTable(ColorAdjustType type, ReadOnlySpan<ColorMap> map) => AdjustmentsOf(type).RemapTable = Copy(map);

    public void SetRemapTable(ReadOnlySpan<(Color OldColor, Color NewColor)> map) => SetRemapTable(ColorAdjustType.Default, map);

    public void SetRemapTable(ColorAdjustType type, ReadOnlySpan<(Color OldColor, Color NewColor)> map)
    {
        var table = new ColorMap[map.Length];
        for (var i = 0; i < map.Length; i++) table[i] = new ColorMap { OldColor = map[i].OldColor, NewColor = map[i].NewColor };

        AdjustmentsOf(type).RemapTable = table;
    }

    public void SetThreshold(float threshold) => SetThreshold(threshold, ColorAdjustType.Default);

    public void SetThreshold(float threshold, ColorAdjustType type) => AdjustmentsOf(type).Threshold = threshold;

    public void SetWrapMode(WrapMode mode) => SetWrapMode(mode, Color.Empty, false);

    public void SetWrapMode(WrapMode mode, Color color) => SetWrapMode(mode, color, false);

    public void SetWrapMode(WrapMode mode, Color color, bool clamp)
    {
        _wrapMode = mode;
        _wrapColor = color;
        _wrapClamp = clamp;
    }

    /// <summary>
    /// Builds the SkiaSharp filter that recolours a drawn pixel the way the
    /// adjustments of <paramref name="type"/>, or of Default when it has none
    /// of its own, describe; null when nothing applies.
    /// </summary>
    /// <summary>What a drawing does outside the image: tile, mirror, or a colour.</summary>
    internal (WrapMode Mode, Color Color) Wrap => (_wrapMode, _wrapColor);

    internal SKColorFilter? ToColorFilter(ColorAdjustType type)
    {
        var effective = Resolve(type);
        return effective?.ToColorFilter();
    }

    private void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// The colours of a remap table, copied: GDI+ takes them at the call, so a
    /// ColorMap changed afterwards must not change the adjustment.
    /// </summary>
    private static ColorMap[] Copy(ReadOnlySpan<ColorMap> map)
    {
        var copy = new ColorMap[map.Length];

        for (var i = 0; i < map.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(map[i]);
            copy[i] = new ColorMap { OldColor = map[i].OldColor, NewColor = map[i].NewColor };
        }

        return copy;
    }

    /// <summary>
    /// Removes one setting of a category. Clearing what was never set creates
    /// nothing, and a category left with no settings at all goes back to
    /// using Default's, as one that never had any.
    /// </summary>
    private void Clear(ColorAdjustType type, Action<Adjustments> clear)
    {
        var index = RequireCategory(type);
        if (_byType[index] is not { } settings) return;

        clear(settings);
        if (settings.IsEmpty) _byType[index] = null;
    }

    private Adjustments AdjustmentsOf(ColorAdjustType type) => _byType[RequireCategory(type)] ??= new Adjustments();

    private Adjustments? TryGet(ColorAdjustType type) => _byType[RequireCategory(type)];

    private static int RequireCategory(ColorAdjustType type)
    {
        var index = (int)type;
        if (index < 0 || index > 4) throw new ArgumentException("Parameter is not valid.", nameof(type));

        return index;
    }

    /// <summary>
    /// The settings that apply to <paramref name="type"/>: its own when it has
    /// any, Default's otherwise; null when it is an explicit no-op.
    /// </summary>
    /// <remarks>
    /// All or nothing, as GDI+ documents it: a category with even one setting
    /// of its own takes nothing from Default. A colour matrix set for Default
    /// and a gamma set for Bitmap give bitmaps the gamma alone.
    /// </remarks>
    private Effective? Resolve(ColorAdjustType type)
    {
        var settings = TryGet(type) ?? (type == ColorAdjustType.Default ? null : TryGet(ColorAdjustType.Default));

        if (settings is null || settings.NoOp) return null;

        return new Effective(
            settings.RemapTable,
            settings.ColorKey,
            settings.ColorMatrix,
            settings.ColorMatrixFlag,
            settings.Threshold,
            settings.Gamma);
    }

    /// <summary>The adjustments set for one <see cref="ColorAdjustType"/>.</summary>
    private sealed class Adjustments
    {
        public ColorMap[]? RemapTable;
        public (Color Low, Color High)? ColorKey;
        public ColorMatrix? ColorMatrix;
        public ColorMatrixFlag ColorMatrixFlag;
        public float? Threshold;
        public float? Gamma;
        public bool NoOp;

        public bool IsEmpty => RemapTable is null && ColorKey is null && ColorMatrix is null && Threshold is null && Gamma is null && !NoOp;

        public Adjustments Clone() => new()
        {
            RemapTable = RemapTable is null ? null : Copy(RemapTable),
            ColorKey = ColorKey,
            ColorMatrix = ColorMatrix,
            ColorMatrixFlag = ColorMatrixFlag,
            Threshold = Threshold,
            Gamma = Gamma,
            NoOp = NoOp,
        };
    }

    /// <summary>The settings that apply to one category.</summary>
    private readonly record struct Effective(
        ColorMap[]? RemapTable,
        (Color Low, Color High)? ColorKey,
        ColorMatrix? ColorMatrix,
        ColorMatrixFlag ColorMatrixFlag,
        float? Threshold,
        float? Gamma)
    {
        public SKColorFilter? ToColorFilter()
        {
            SKColorFilter? filter = null;

            if (RemapTable is { Length: > 0 })
            {
                filter = ImageAttributesColorFilters.Then(filter, ImageAttributesColorFilters.RemapTable(RemapTable));
            }

            if (ColorKey is { } key)
            {
                filter = ImageAttributesColorFilters.Then(filter, ImageAttributesColorFilters.ColorKey(key.Low, key.High));
            }

            if (ColorMatrix is not null)
            {
                filter = ImageAttributesColorFilters.Then(filter, ImageAttributesColorFilters.Matrix(ColorMatrix, ColorMatrixFlag));
            }

            if (Threshold is { } threshold)
            {
                filter = ImageAttributesColorFilters.Then(filter, ImageAttributesColorFilters.Threshold(threshold));
            }

            if (Gamma is { } gamma)
            {
                filter = ImageAttributesColorFilters.Then(filter, ImageAttributesColorFilters.Gamma(gamma));
            }

            return filter;
        }

        /// <summary>The plain-C# version of <see cref="ToColorFilter"/>, for the one colour at a time <see cref="ImageAttributes.GetAdjustedPalette"/> adjusts.</summary>
        public Color Apply(Color color)
        {
            if (RemapTable is not null)
            {
                foreach (var entry in RemapTable)
                {
                    if (entry.OldColor.ToArgb() != color.ToArgb()) continue;

                    color = entry.NewColor;
                    break;
                }
            }

            if (ColorKey is { } key &&
                color.R >= key.Low.R && color.R <= key.High.R &&
                color.G >= key.Low.G && color.G <= key.High.G &&
                color.B >= key.Low.B && color.B <= key.High.B)
            {
                color = Color.FromArgb(0, color.R, color.G, color.B);
            }

            if (ColorMatrix is not null)
            {
                if (ColorMatrixFlag == ColorMatrixFlag.AltGrays)
                {
                    throw new NotImplementedException(
                        "ColorMatrixFlag.AltGrays (a second matrix for pixels where R=G=B) is not implemented yet.");
                }

                if (ColorMatrixFlag != ColorMatrixFlag.SkipGrays || color.R != color.G || color.G != color.B)
                {
                    color = ApplyMatrix(color, ColorMatrix);
                }
            }

            if (Threshold is { } threshold) color = ApplyThreshold(color, threshold);

            if (Gamma is { } gamma) color = ApplyGamma(color, gamma);

            return color;
        }

        private static Color ApplyMatrix(Color color, ColorMatrix matrix)
        {
            var r = color.R / 255f;
            var g = color.G / 255f;
            var b = color.B / 255f;
            var a = color.A / 255f;

            float Output(int channel) =>
                r * matrix.Coefficient(0, channel) + g * matrix.Coefficient(1, channel) +
                b * matrix.Coefficient(2, channel) + a * matrix.Coefficient(3, channel) + matrix.Coefficient(4, channel);

            return Color.FromArgb(ToByte(Output(3)), ToByte(Output(0)), ToByte(Output(1)), ToByte(Output(2)));
        }

        private static Color ApplyThreshold(Color color, float threshold)
        {
            var cut = (byte)Math.Clamp(MathF.Round(threshold * 255f), 0, 255);
            return Color.FromArgb(color.A, Cut(color.R), Cut(color.G), Cut(color.B));

            byte Cut(byte channel) => channel >= cut ? (byte)255 : (byte)0;
        }

        private static Color ApplyGamma(Color color, float gamma)
        {
            var exponent = 1f / gamma;
            return Color.FromArgb(color.A, Correct(color.R), Correct(color.G), Correct(color.B));

            byte Correct(byte channel) => ToByte(MathF.Pow(channel / 255f, exponent));
        }

        private static byte ToByte(float normalized) => (byte)Math.Clamp(MathF.Round(normalized * 255f), 0, 255);
    }
}
