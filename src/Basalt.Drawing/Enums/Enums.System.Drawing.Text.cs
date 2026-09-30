// The enumerations of this namespace, with the names and values of Microsoft's
// System.Drawing.Common reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Drawing.Text;

public enum GenericFontFamilies
{
    Serif = 0,
    SansSerif = 1,
    Monospace = 2,
}

public enum HotkeyPrefix
{
    None = 0,
    Show = 1,
    Hide = 2,
}

public enum TextRenderingHint
{
    SystemDefault = 0,
    SingleBitPerPixelGridFit = 1,
    SingleBitPerPixel = 2,
    AntiAliasGridFit = 3,
    AntiAlias = 4,
    ClearTypeGridFit = 5,
}
