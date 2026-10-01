namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// One contiguous span of text sharing a <see cref="CharacterFormat"/>. A run
/// that is exactly a paragraph break ("\r\n", "\n" or "\r") also carries the
/// <see cref="ParagraphFormat"/> of the paragraph it closes: RTF's \pard...\par
/// group ends at the paragraph mark, and this mirrors that.
/// </summary>
internal sealed class RichRun(string text, CharacterFormat format, ParagraphFormat? paragraphBreak = null)
{
    public string Text { get; set; } = text;

    public CharacterFormat Format { get; set; } = format;

    public ParagraphFormat? ParagraphBreak { get; set; } = paragraphBreak;

    public int Length => Text.Length;

    public bool IsParagraphBreak => ParagraphBreak is not null;
}
