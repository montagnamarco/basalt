using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>A per-channel 256-entry lookup table, applied exactly as given: GDI+ documents each table as exactly 256 bytes, one new value per old one.</summary>
public class ColorLookupTableEffect : Effect
{
    private readonly byte[] _red;
    private readonly byte[] _green;
    private readonly byte[] _blue;
    private readonly byte[] _alpha;

    public ColorLookupTableEffect(byte[] redLookupTable, byte[] greenLookupTable, byte[] blueLookupTable, byte[] alphaLookupTable)
        : this(
            (ReadOnlySpan<byte>)Required(redLookupTable, nameof(redLookupTable)),
            (ReadOnlySpan<byte>)Required(greenLookupTable, nameof(greenLookupTable)),
            (ReadOnlySpan<byte>)Required(blueLookupTable, nameof(blueLookupTable)),
            (ReadOnlySpan<byte>)Required(alphaLookupTable, nameof(alphaLookupTable)))
    {
    }

    public ColorLookupTableEffect(ReadOnlySpan<byte> redLookupTable, ReadOnlySpan<byte> greenLookupTable, ReadOnlySpan<byte> blueLookupTable, ReadOnlySpan<byte> alphaLookupTable)
    {
        _red = Copy(redLookupTable, nameof(redLookupTable));
        _green = Copy(greenLookupTable, nameof(greenLookupTable));
        _blue = Copy(blueLookupTable, nameof(blueLookupTable));
        _alpha = Copy(alphaLookupTable, nameof(alphaLookupTable));
    }

    public ReadOnlyMemory<byte> RedLookupTable => _red;

    public ReadOnlyMemory<byte> GreenLookupTable => _green;

    public ReadOnlyMemory<byte> BlueLookupTable => _blue;

    public ReadOnlyMemory<byte> AlphaLookupTable => _alpha;

    internal override SKImageFilter CreateFilter() =>
        SKImageFilter.CreateColorFilter(SKColorFilter.CreateTable(_alpha, _red, _green, _blue));

    private static byte[] Required(byte[]? table, string name)
    {
        ArgumentNullException.ThrowIfNull(table, name);
        return table;
    }

    private static byte[] Copy(ReadOnlySpan<byte> table, string name)
    {
        if (table.Length != 256) throw new ArgumentException("A lookup table needs exactly 256 entries.", name);
        return table.ToArray();
    }
}
