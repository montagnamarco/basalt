using System.Text;

namespace Basalt.Core.Text;

/// <summary>
/// What encoding a file is in, and how to say so.
///
/// The status bar said "UTF-8" whatever the file held, which is a label
/// rather than a fact. A byte order mark says for certain; without one, a
/// file that decodes cleanly as UTF-8 is treated as UTF-8, which is what it
/// almost always is.
/// </summary>
public static class FileEncoding
{
    /// <summary>The encodings a file can be reopened as.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "Windows-1252", "ASCII"];

    /// <summary>Works out what a file is in, from its first bytes.</summary>
    public static string Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return "UTF-8 with BOM";

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return "UTF-16 LE";
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return "UTF-16 BE";

        // No mark: UTF-8 unless the bytes cannot be read as it, in which case
        // Windows-1252 is the likeliest thing a file of the era holds.
        return IsValidUtf8(bytes) ? "UTF-8" : "Windows-1252";
    }

    /// <summary>Works out what a file on disk is in.</summary>
    public static async Task<string> DetectAsync(string path, CancellationToken ct = default)
    {
        try
        {
            return Detect(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "UTF-8";
        }
    }

    /// <summary>The encoding a name stands for.</summary>
    public static Encoding For(string name) => name switch
    {
        "UTF-8 with BOM" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        "UTF-16 LE" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        "UTF-16 BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),

        // Latin-1 rather than the real code page, which needs a provider
        // registered; the two differ only from 0x80 to 0x9F.
        "Windows-1252" => Encoding.Latin1,

        "ASCII" => Encoding.ASCII,
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
    };

    /// <summary>
    /// Whether bytes decode as UTF-8 without error.
    ///
    /// Checked by decoding strictly rather than by reading the bytes myself:
    /// the rules for continuation bytes are exactly what a strict decoder
    /// already knows.
    /// </summary>
    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
