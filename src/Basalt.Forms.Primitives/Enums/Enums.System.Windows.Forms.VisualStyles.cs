// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms.Primitives reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms.VisualStyles;

public enum TextMetricsCharacterSet
{
    Ansi = 0,
    Baltic = 186,
    ChineseBig5 = 136,
    Default = 1,
    EastEurope = 238,
    Gb2312 = 134,
    Greek = 161,
    Hangul = 129,
    Mac = 77,
    Oem = 255,
    Russian = 204,
    ShiftJis = 128,
    Symbol = 2,
    Turkish = 162,
    Vietnamese = 163,
    Johab = 130,
    Arabic = 178,
    Hebrew = 177,
    Thai = 222,
}

[Flags]
public enum TextMetricsPitchAndFamilyValues
{
    FixedPitch = 1,
    Vector = 2,
    TrueType = 4,
    Device = 8,
}

public enum ThemeSizeType
{
    Minimum = 0,
    True = 1,
    Draw = 2,
}
