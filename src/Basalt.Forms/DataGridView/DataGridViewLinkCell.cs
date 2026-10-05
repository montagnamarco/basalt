using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A cell painted as a hyperlink: its text in LinkColor, in ActiveLinkColor while the mouse is
/// pressed on it, or in VisitedLinkColor once LinkVisited is set (TrackVisitedState sets it
/// automatically from OnMouseUp, the same place Microsoft's own link cell does it - it does not
/// override OnContentClick, unlike DataGridViewCheckBoxCell).
/// </summary>
public partial class DataGridViewLinkCell : DataGridViewCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    private bool _pressed;

    public DataGridViewLinkCell()
    {
    }

    public Color ActiveLinkColor { get; set; } = SystemColors.HotTrack;

    public override Type? EditType => null;

    internal override bool UsesTextEditorInternal => false;

    public override Type? FormattedValueType => typeof(string);

    [DefaultValue(LinkBehavior.SystemDefault)]
    public LinkBehavior LinkBehavior { get; set; } = LinkBehavior.SystemDefault;

    public Color LinkColor { get; set; } = SystemColors.HotTrack;

    public bool LinkVisited { get; set; }

    [DefaultValue(true)]
    public bool TrackVisitedState { get; set; } = true;

    [DefaultValue(false)]
    public bool UseColumnTextForLinkValue { get; set; }

    public override Type ValueType => base.ValueType ?? typeof(object);

    public Color VisitedLinkColor { get; set; } = SystemColors.ControlDarkDark;

    public override object Clone()
    {
        var clone = (DataGridViewLinkCell)base.Clone();
        clone.ActiveLinkColor = ActiveLinkColor;
        clone.LinkBehavior = LinkBehavior;
        clone.LinkColor = LinkColor;
        clone.LinkVisited = LinkVisited;
        clone.TrackVisitedState = TrackVisitedState;
        clone.UseColumnTextForLinkValue = UseColumnTextForLinkValue;
        clone.VisitedLinkColor = VisitedLinkColor;

        return clone;
    }

    protected override object? GetValue(int rowIndex) =>
        UseColumnTextForLinkValue && OwningColumn is DataGridViewLinkColumn { Text: { Length: > 0 } text } ? text : base.GetValue(rowIndex);

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex) =>
        FormattedValue is not null && DataGridView is not null && ColumnIndex >= 0 && rowIndex >= 0
            ? new Rectangle(Point.Empty, GetSize(rowIndex))
            : Rectangle.Empty;

    protected override Size GetPreferredSize(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex, Size constraintSize)
    {
        var text = FormattedValue?.ToString() ?? string.Empty;
        var measured = TextRenderer.MeasureText(text, cellStyle.Font ?? DataGridView?.Font);
        return new Size(measured.Width + 4 + cellStyle.Padding.Horizontal,
            measured.Height + 2 + cellStyle.Padding.Vertical);
    }

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        // A direct cell hook invocation has no originating MouseEventArgs and reports None;
        // it still represents the same press that the grid normally sends with Left.
        _pressed = e.Button is (MouseButtons.Left or MouseButtons.None) && ContentBounds.Contains(e.X, e.Y);
        DataGridView?.InvalidateCellCore(this);
    }

    protected override void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        if (TrackVisitedState && ContentBounds.Contains(e.X, e.Y)) LinkVisited = true;
        DataGridView?.InvalidateCellCore(this);
    }

    protected override void Paint(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            var selected = (cellState & DataGridViewElementStates.Selected) != 0;
            using var back = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(back, cellBounds);
        }

        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0 && formattedValue is { } text)
        {
            var color = _pressed ? ActiveLinkColor : LinkVisited ? VisitedLinkColor : LinkColor;
            var flags = TextAlignmentFlags(cellStyle.Alignment);
            var baseFont = cellStyle.Font ?? DataGridView?.Font;
            var underline = LinkBehavior is LinkBehavior.SystemDefault or LinkBehavior.AlwaysUnderline
                || LinkBehavior == LinkBehavior.HoverUnderline && _hovered;
            using var underlined = baseFont is null || !underline ? null : new Font(baseFont, baseFont.Style | FontStyle.Underline);

            TextRenderer.DrawText(graphics, text.ToString(), underlined ?? baseFont, cellBounds, color, flags);
        }

        if ((paintParts & DataGridViewPaintParts.Border) != 0) PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
    }

    public override string ToString() => $"DataGridViewLinkCell {{ ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
}
