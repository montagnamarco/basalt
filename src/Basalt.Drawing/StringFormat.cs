using System.Drawing.Text;

namespace System.Drawing;

/// <summary>How text is laid out in its rectangle: alignment, wrapping, trimming, tabs.</summary>
/// <remarks>
/// GenericDefault leaves a sixth of an em of room on each side of a line and
/// trims at a character; GenericTypographic leaves none, never clips and only
/// lays out whole lines. Both are fresh copies on every call, as in GDI+, so a
/// caller changing one does not change it for everybody.
/// </remarks>
public sealed class StringFormat : MarshalByRefObject, ICloneable, IDisposable
{
    private float _firstTabOffset;
    private float[] _tabStops = [];

    public StringFormat()
        : this(0, 0)
    {
    }

    public StringFormat(StringFormatFlags options)
        : this(options, 0)
    {
    }

    public StringFormat(StringFormatFlags options, int language)
    {
        FormatFlags = options;
        DigitSubstitutionLanguage = language;
    }

    public StringFormat(StringFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        FormatFlags = format.FormatFlags;
        Alignment = format.Alignment;
        LineAlignment = format.LineAlignment;
        HotkeyPrefix = format.HotkeyPrefix;
        Trimming = format.Trimming;
        DigitSubstitutionLanguage = format.DigitSubstitutionLanguage;
        DigitSubstitutionMethod = format.DigitSubstitutionMethod;
        IsTypographic = format.IsTypographic;
        _firstTabOffset = format._firstTabOffset;
        _tabStops = (float[])format._tabStops.Clone();
        MeasurableCharacterRanges = (CharacterRange[])format.MeasurableCharacterRanges.Clone();
    }

    ~StringFormat() => Dispose(false);

    public static StringFormat GenericDefault => new() { Trimming = StringTrimming.Character };

    public static StringFormat GenericTypographic => new(StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip)
    {
        Trimming = StringTrimming.None,
        IsTypographic = true,
    };

    public StringFormatFlags FormatFlags { get; set; }

    public StringAlignment Alignment
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new System.ComponentModel.InvalidEnumArgumentException(nameof(value), (int)value, typeof(StringAlignment));
    }

    public StringAlignment LineAlignment
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new System.ComponentModel.InvalidEnumArgumentException(nameof(value), (int)value, typeof(StringAlignment));
    }

    public HotkeyPrefix HotkeyPrefix { get; set; }

    public StringTrimming Trimming { get; set; } = StringTrimming.Character;

    public int DigitSubstitutionLanguage { get; private set; }

    public StringDigitSubstitute DigitSubstitutionMethod { get; private set; }

    /// <summary>No padding at the ends of lines: GenericTypographic and its copies.</summary>
    internal bool IsTypographic { get; private set; }

    internal CharacterRange[] MeasurableCharacterRanges { get; private set; } = [];

    internal float FirstTabOffset => _firstTabOffset;

    internal float[] TabStopsOrEmpty => _tabStops;

    public void SetTabStops(float firstTabOffset, float[] tabStops)
    {
        ArgumentNullException.ThrowIfNull(tabStops);

        if (firstTabOffset < 0) throw new ArgumentException("Parameter is not valid.", nameof(firstTabOffset));

        _firstTabOffset = firstTabOffset;
        _tabStops = (float[])tabStops.Clone();
    }

    public float[] GetTabStops(out float firstTabOffset)
    {
        firstTabOffset = _firstTabOffset;

        return (float[])_tabStops.Clone();
    }

    /// <summary>Recorded for MeasureCharacterRanges; digits are drawn as they are written.</summary>
    public void SetDigitSubstitution(int language, StringDigitSubstitute substitute)
    {
        DigitSubstitutionLanguage = language;
        DigitSubstitutionMethod = substitute;
    }

    /// <summary>The runs MeasureCharacterRanges reports regions for; GDI+ allows at most 32.</summary>
    public void SetMeasurableCharacterRanges(CharacterRange[] ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        if (ranges.Length > 32) throw new OverflowException();

        MeasurableCharacterRanges = (CharacterRange[])ranges.Clone();
    }

    public object Clone() => new StringFormat(this);

    public override string ToString() => $"[StringFormat, FormatFlags={FormatFlags}]";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }
}
