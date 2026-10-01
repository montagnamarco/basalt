namespace System.Drawing.Printing;

/// <summary>The dimensions of a sheet of paper, in hundredths of an inch.</summary>
public class PaperSize
{
    private PaperKind _kind = PaperKind.Custom;

    public PaperSize()
    {
    }

    public PaperSize(string paperName, int width, int height)
    {
        PaperName = paperName;
        Width = width;
        Height = height;
        _kind = PaperKind.Custom;
    }

    public int Height { get; set; }

    public PaperKind Kind => _kind;

    public string PaperName { get; set; } = string.Empty;

    // There is no raw driver code here, only the PaperKind enum: a value the
    // enum does not know becomes Custom, same as GDI+ reports for a driver's
    // own custom size.
    public int RawKind
    {
        get => (int)_kind;
        set => _kind = Enum.IsDefined(typeof(PaperKind), value) ? (PaperKind)value : PaperKind.Custom;
    }

    public int Width { get; set; }

    public override string ToString() =>
        $"[PaperSize {PaperName} Kind={Kind} Height={Height} Width={Width}]";
}
