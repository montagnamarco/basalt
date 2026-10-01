using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing.Text;

/// <summary>Fonts an application loads from files or memory for its own use.</summary>
public sealed class PrivateFontCollection : FontCollection
{
    private readonly List<SKTypeface> _faces = [];

    public PrivateFontCollection()
    {
    }

    public void AddFontFile(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        if (!File.Exists(filename)) throw new FileNotFoundException(null, filename);

        _faces.Add(SKTypeface.FromFile(filename) ?? throw new ArgumentException("Parameter is not valid.", nameof(filename)));
    }

    /// <summary>Copies the font out of the caller's memory, which GDI+ also lets go of after the call.</summary>
    public void AddMemoryFont(IntPtr memory, int length)
    {
        if (memory == IntPtr.Zero || length <= 0) throw new ArgumentException("Parameter is not valid.");

        var bytes = new byte[length];
        Marshal.Copy(memory, bytes, 0, length);

        using var data = SKData.CreateCopy(bytes);
        _faces.Add(SKTypeface.FromData(data) ?? throw new ArgumentException("Parameter is not valid."));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var face in _faces) face.Dispose();

            _faces.Clear();
        }

        base.Dispose(disposing);
    }

    internal override IEnumerable<string> FamilyNames() =>
        _faces.Select(face => face.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The loaded face closest to the style: the exact one when it was loaded,
    /// otherwise any face of the family, which the drawing then emboldens or
    /// slants as GDI+ simulates a missing style.
    /// </summary>
    internal override SKTypeface? Typeface(string family, FontStyle style)
    {
        var faces = _faces.Where(face => string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase)).ToList();
        if (faces.Count == 0) return null;

        var bold = (style & FontStyle.Bold) != 0;
        var italic = (style & FontStyle.Italic) != 0;

        return faces.FirstOrDefault(face => face.IsBold == bold && face.IsItalic == italic) ?? faces[0];
    }
}
