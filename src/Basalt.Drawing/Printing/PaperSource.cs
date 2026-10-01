namespace System.Drawing.Printing;

/// <summary>Which tray or feed a printer takes paper from.</summary>
public class PaperSource
{
    private PaperSourceKind _kind = PaperSourceKind.AutomaticFeed;

    public PaperSourceKind Kind => _kind;

    // Same convention as PaperSize.RawKind: an undefined raw value becomes Custom.
    public int RawKind
    {
        get => (int)_kind;
        set => _kind = Enum.IsDefined(typeof(PaperSourceKind), value) ? (PaperSourceKind)value : PaperSourceKind.Custom;
    }

    public string SourceName { get; set; } = string.Empty;

    public override string ToString() => $"[PaperSource {Kind} SourceName={SourceName}]";
}
