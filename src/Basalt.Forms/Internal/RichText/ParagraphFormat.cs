namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// The formatting of one paragraph: alignment, the indents RichTextBox
/// exposes as SelectionIndent/SelectionRightIndent/SelectionHangingIndent,
/// a bullet, and its tab stops. One instance belongs to exactly one
/// paragraph; editing clones it so paragraphs never alias each other's format.
/// </summary>
internal sealed class ParagraphFormat
{
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    public int Indent { get; set; }

    public int RightIndent { get; set; }

    public int HangingIndent { get; set; }

    public bool Bullet { get; set; }

    public int BulletIndent { get; set; }

    public int[] Tabs { get; set; } = [];

    public ParagraphFormat Clone() => new()
    {
        Alignment = Alignment,
        Indent = Indent,
        RightIndent = RightIndent,
        HangingIndent = HangingIndent,
        Bullet = Bullet,
        BulletIndent = BulletIndent,
        Tabs = (int[])Tabs.Clone(),
    };
}
