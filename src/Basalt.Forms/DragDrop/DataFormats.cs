namespace System.Windows.Forms;

/// <summary>
/// The names clipboard and drag-and-drop data is tagged with. The predefined
/// ones keep the ids Windows gives its built-in clipboard formats, so code that
/// compares a <see cref="Format"/>'s <see cref="Format.Id"/> across calls sees
/// the same number every time; a name nobody registered yet gets the next free
/// id, starting where Windows reserves ids for applications (0xC000).
/// </summary>
public static class DataFormats
{
    public static readonly string Text = "Text";
    public static readonly string UnicodeText = "UnicodeText";
    public static readonly string Dib = "DeviceIndependentBitmap";
    public static readonly string Bitmap = "Bitmap";
    public static readonly string EnhancedMetafile = "EnhancedMetafile";
    public static readonly string MetafilePict = "MetaFilePict";
    public static readonly string SymbolicLink = "SymbolicLink";
    public static readonly string Dif = "DataInterchangeFormat";
    public static readonly string Tiff = "TaggedImageFileFormat";
    public static readonly string OemText = "OEMText";
    public static readonly string Palette = "Palette";
    public static readonly string PenData = "PenData";
    public static readonly string Riff = "RIFF";
    public static readonly string WaveAudio = "WaveAudio";
    public static readonly string FileDrop = "FileDrop";
    public static readonly string Locale = "Locale";
    public static readonly string Html = "HTML Format";
    public static readonly string Rtf = "Rich Text Format";
    public static readonly string CommaSeparatedValue = "CSV";
    public static readonly string StringFormat = "System.String";
    public static readonly string Serializable = "PersistentObject";

    private static readonly Dictionary<string, Format> FormatsByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Format> FormatsById = [];
    private static int s_nextPrivateId = 0xC000;

    static DataFormats()
    {
        Register(Text, 1);
        Register(Bitmap, 2);
        Register(MetafilePict, 3);
        Register(SymbolicLink, 4);
        Register(Dif, 5);
        Register(Tiff, 6);
        Register(OemText, 7);
        Register(Dib, 8);
        Register(Palette, 9);
        Register(PenData, 10);
        Register(Riff, 11);
        Register(WaveAudio, 12);
        Register(UnicodeText, 13);
        Register(EnhancedMetafile, 14);
        Register(FileDrop, 15);
        Register(Locale, 16);
    }

    private static void Register(string name, int id)
    {
        var format = new Format(name, id);
        FormatsByName[name] = format;
        FormatsById[id] = format;
    }

    /// <summary>The format for a name, giving it an id the first time it is seen.</summary>
    public static Format GetFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        lock (FormatsByName)
        {
            if (FormatsByName.TryGetValue(format, out var existing)) return existing;

            var created = new Format(format, s_nextPrivateId++);
            FormatsByName[format] = created;
            FormatsById[created.Id] = created;

            return created;
        }
    }

    /// <summary>The format for an id already registered, or a placeholder name for one that is not.</summary>
    public static Format GetFormat(int id)
    {
        lock (FormatsByName)
        {
            if (FormatsById.TryGetValue(id, out var existing)) return existing;

            var created = new Format($"Format{id}", id);
            FormatsById[id] = created;
            FormatsByName[created.Name] = created;

            return created;
        }
    }

    /// <summary>A format's name paired with the id it is known by.</summary>
    public class Format
    {
        public Format(string name, int id)
        {
            Name = name;
            Id = id;
        }

        public int Id { get; }

        public string Name { get; }
    }
}
