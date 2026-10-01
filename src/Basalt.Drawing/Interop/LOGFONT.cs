using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System.Drawing.Interop;

/// <summary>
/// The Windows LOGFONTW structure, field for field, so that code passing one
/// to Font.FromLogFont or reading one back keeps working.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct LOGFONT
{
    public int lfHeight;
    public int lfWidth;
    public int lfEscapement;
    public int lfOrientation;
    public int lfWeight;
    public byte lfItalic;
    public byte lfUnderline;
    public byte lfStrikeOut;
    public byte lfCharSet;
    public byte lfOutPrecision;
    public byte lfClipPrecision;
    public byte lfQuality;
    public byte lfPitchAndFamily;
    private fixed char _lfFaceName[32];

    [UnscopedRef]
    public Span<char> lfFaceName => MemoryMarshal.CreateSpan(ref _lfFaceName[0], 32);
}
