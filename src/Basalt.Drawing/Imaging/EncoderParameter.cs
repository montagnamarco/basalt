using System.Runtime.InteropServices;

namespace System.Drawing.Imaging;

/// <summary>One value passed to an encoder through <see cref="EncoderParameters"/>.</summary>
/// <remarks>
/// GDI+ never gave callers a way to read a value back out of an
/// <see cref="EncoderParameter"/> (only <see cref="Encoder"/>, <see cref="Type"/>
/// and <see cref="NumberOfValues"/> are public): the value only ever flows one
/// way, from application code into the native encoder. This library's own
/// <c>Image.Save</c> is that encoder, and reads the value back through the
/// internal <see cref="Values"/>/<see cref="Text"/> it keeps for itself.
///
/// The two-int and four-int constructors match GDI+'s documented Rational and
/// RationalRange value types (a numerator/denominator pair, and a pair of
/// pairs). The three-int constructor has no documented counterpart in either
/// form; GDI+'s own header comments describe only the 2-int and 4-int shapes.
/// It is stored as three raw values here so the overload exists and behaves
/// predictably; its true GDI+ semantics could not be confirmed without Windows.
/// </remarks>
public sealed class EncoderParameter : IDisposable
{
    public EncoderParameter(Encoder encoder, byte value)
        : this(encoder, [value], false)
    {
    }

    public EncoderParameter(Encoder encoder, byte value, bool undefined)
        : this(encoder, [value], undefined)
    {
    }

    public EncoderParameter(Encoder encoder, byte[] value)
        : this(encoder, value, false)
    {
    }

    public EncoderParameter(Encoder encoder, byte[] value, bool undefined)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(value);

        Encoder = encoder;
        NumberOfValues = value.Length;
        Type = ValueType = undefined ? EncoderParameterValueType.ValueTypeUndefined : EncoderParameterValueType.ValueTypeByte;
        Values = Array.ConvertAll(value, b => (long)b);
    }

    public EncoderParameter(Encoder encoder, short value)
        : this(encoder, [value])
    {
    }

    public EncoderParameter(Encoder encoder, short[] value)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(value);

        Encoder = encoder;
        NumberOfValues = value.Length;
        Type = ValueType = EncoderParameterValueType.ValueTypeShort;
        Values = Array.ConvertAll(value, v => (long)v);
    }

    public EncoderParameter(Encoder encoder, long value)
        : this(encoder, [value])
    {
    }

    public EncoderParameter(Encoder encoder, long[] value)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(value);

        Encoder = encoder;
        NumberOfValues = value.Length;
        Type = ValueType = EncoderParameterValueType.ValueTypeLong;
        Values = (long[])value.Clone();
    }

    public EncoderParameter(Encoder encoder, long rangeBegin, long rangeEnd)
        : this(encoder, [rangeBegin], [rangeEnd])
    {
    }

    public EncoderParameter(Encoder encoder, long[] rangeBegin, long[] rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(rangeBegin);
        ArgumentNullException.ThrowIfNull(rangeEnd);
        if (rangeBegin.Length != rangeEnd.Length) throw new ArgumentException("Parameter is not valid.");

        Encoder = encoder;
        NumberOfValues = rangeBegin.Length;
        Type = ValueType = EncoderParameterValueType.ValueTypeLongRange;
        Values = Interleave(rangeBegin, rangeEnd);
    }

    public EncoderParameter(Encoder encoder, string value)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(value);

        Encoder = encoder;
        // GDI+ counts the ASCII string's terminating null as part of the value.
        NumberOfValues = value.Length + 1;
        Type = ValueType = EncoderParameterValueType.ValueTypeAscii;
        Values = [];
        Text = value;
    }

    public EncoderParameter(Encoder encoder, int numerator, int denominator)
        : this(encoder, [numerator], [denominator])
    {
    }

    public EncoderParameter(Encoder encoder, int[] numerator, int[] denominator)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(numerator);
        ArgumentNullException.ThrowIfNull(denominator);

        Encoder = encoder;
        NumberOfValues = numerator.Length;
        Type = ValueType = EncoderParameterValueType.ValueTypeRational;
        Values = Interleave(numerator, denominator);
    }

    public EncoderParameter(Encoder encoder, int numerator1, int denominator1, int numerator2, int denominator2)
        : this(encoder, [numerator1], [denominator1], [numerator2], [denominator2])
    {
    }

    public EncoderParameter(Encoder encoder, int[] numerator1, int[] denominator1, int[] numerator2, int[] denominator2)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(numerator1);
        ArgumentNullException.ThrowIfNull(denominator1);
        ArgumentNullException.ThrowIfNull(numerator2);
        ArgumentNullException.ThrowIfNull(denominator2);

        Encoder = encoder;
        NumberOfValues = numerator1.Length;
        Type = ValueType = EncoderParameterValueType.ValueTypeRationalRange;
        Values = Interleave(Interleave(numerator1, denominator1), Interleave(numerator2, denominator2));
    }

    public EncoderParameter(Encoder encoder, int value1, int value2, int value3)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        Encoder = encoder;
        NumberOfValues = 3;
        Type = ValueType = EncoderParameterValueType.ValueTypeLong;
        Values = [value1, value2, value3];
    }

    public EncoderParameter(Encoder encoder, int numberOfValues, EncoderParameterValueType type, IntPtr value)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        if (numberOfValues < 0) throw new ArgumentException("Parameter is not valid.");

        Encoder = encoder;
        NumberOfValues = numberOfValues;
        Type = ValueType = type;
        Values = ReadRaw(value, numberOfValues, type);
    }

    public Encoder Encoder { get; set; }

    public int NumberOfValues { get; }

    public EncoderParameterValueType Type { get; }

    public EncoderParameterValueType ValueType { get; }

    /// <summary>The value, coerced to long, for every numeric parameter type.</summary>
    internal long[] Values { get; }

    /// <summary>The value of a ValueTypeAscii parameter; null for every other type.</summary>
    internal string? Text { get; }

    public void Dispose() => GC.SuppressFinalize(this);

    ~EncoderParameter()
    {
    }

    private static long[] Interleave(int[] a, int[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Parameter is not valid.");

        var values = new long[a.Length * 2];
        for (var i = 0; i < a.Length; i++)
        {
            values[i * 2] = a[i];
            values[i * 2 + 1] = b[i];
        }

        return values;
    }

    private static long[] Interleave(long[] a, long[] b)
    {
        var values = new long[a.Length + b.Length];
        Array.Copy(a, values, a.Length);
        Array.Copy(b, 0, values, a.Length, b.Length);
        return values;
    }

    /// <summary>
    /// Reads back the raw-pointer constructor's buffer, honouring the byte width
    /// each <see cref="EncoderParameterValueType"/> names.
    /// </summary>
    private static long[] ReadRaw(IntPtr value, int count, EncoderParameterValueType type)
    {
        var values = new long[count];
        var width = type switch
        {
            EncoderParameterValueType.ValueTypeByte or EncoderParameterValueType.ValueTypeUndefined or EncoderParameterValueType.ValueTypeAscii => 1,
            EncoderParameterValueType.ValueTypeShort => 2,
            EncoderParameterValueType.ValueTypeLong => 4,
            EncoderParameterValueType.ValueTypeRational => 4,
            EncoderParameterValueType.ValueTypeLongRange => 4,
            EncoderParameterValueType.ValueTypeRationalRange => 4,
            EncoderParameterValueType.ValueTypePointer => 8,
            _ => 4,
        };

        for (var i = 0; i < count; i++)
        {
            values[i] = width switch
            {
                1 => Marshal.ReadByte(value, i),
                2 => Marshal.ReadInt16(value, i * width),
                8 => Marshal.ReadInt64(value, i * width),
                _ => Marshal.ReadInt32(value, i * width),
            };
        }

        return values;
    }
}
