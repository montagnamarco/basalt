using System.Globalization;
using System.Text;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Converts between HTML fragments and Windows' byte-offset CF_HTML format.</summary>
internal static class HtmlClipboardFormat
{
    private const string Header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
    private const string Prefix = "<html><body><!--StartFragment-->";
    private const string Suffix = "<!--EndFragment--></body></html>";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string ToPlatform(string html, bool windows)
    {
        if (TryExtractFragment(html, out var fragment)) return windows ? html : fragment;
        return windows ? WrapFragment(html) : html;
    }

    internal static string FromPlatform(string html, bool windows) =>
        !windows && TryExtractFragment(html, out var fragment) ? fragment : html;

    internal static string WrapFragment(string fragment)
    {
        var startHtml = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, Header, 0, 0, 0, 0));
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(Prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(Suffix);
        return string.Format(CultureInfo.InvariantCulture, Header, startHtml, endHtml, startFragment, endFragment)
            + Prefix + fragment + Suffix;
    }

    internal static bool TryExtractFragment(string value, out string fragment)
    {
        fragment = string.Empty;
        if (!value.StartsWith("Version:", StringComparison.Ordinal)) return false;

        var headerEnd = FindHeaderEnd(value);
        if (headerEnd < 0) return false;

        var header = value.AsSpan(0, headerEnd);
        if (!TryReadOffset(header, "StartHTML:", out var startHtml)
            || !TryReadOffset(header, "EndHTML:", out var endHtml)
            || !TryReadOffset(header, "StartFragment:", out var startFragment)
            || !TryReadOffset(header, "EndFragment:", out var endFragment)) return false;

        var bytes = Encoding.UTF8.GetBytes(value);
        if (startFragment < headerEnd || startFragment > endFragment || endFragment > bytes.Length)
            return false;
        var noContext = startHtml == -1 && endHtml == -1;
        if (!noContext && (startHtml < headerEnd || startHtml > startFragment
            || endFragment > endHtml || endHtml > bytes.Length)) return false;
        if ((startHtml == -1) != (endHtml == -1)) return false;

        try
        {
            fragment = StrictUtf8.GetString(bytes.AsSpan(startFragment, endFragment - startFragment));
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool TryReadOffset(ReadOnlySpan<char> header, string field, out int offset)
    {
        offset = 0;
        var position = header.IndexOf(field.AsSpan(), StringComparison.Ordinal);
        if (position < 0) return false;
        var value = header[(position + field.Length)..];
        var end = value.IndexOfAny('\r', '\n');
        if (end >= 0) value = value[..end];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
    }

    private static int FindHeaderEnd(string value)
    {
        var position = 0;
        while (position < value.Length && position < 8192)
        {
            var lineEnd = value.IndexOf('\n', position);
            if (lineEnd < 0) lineEnd = value.Length;
            var line = value.AsSpan(position, lineEnd - position).TrimEnd('\r');
            if (!line.StartsWith("Version:", StringComparison.Ordinal)
                && !line.StartsWith("StartHTML:", StringComparison.Ordinal)
                && !line.StartsWith("EndHTML:", StringComparison.Ordinal)
                && !line.StartsWith("StartFragment:", StringComparison.Ordinal)
                && !line.StartsWith("EndFragment:", StringComparison.Ordinal)
                && !line.StartsWith("StartSelection:", StringComparison.Ordinal)
                && !line.StartsWith("EndSelection:", StringComparison.Ordinal)
                && !line.StartsWith("SourceURL:", StringComparison.Ordinal))
                return position;

            if (lineEnd == value.Length) return -1;
            position = lineEnd + 1;
        }

        return position < 8192 ? position : -1;
    }
}
