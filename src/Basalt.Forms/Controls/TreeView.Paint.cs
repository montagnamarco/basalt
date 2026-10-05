using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class TreeView
{
    /// <summary>Window white (or the control colour when disabled) unless a colour was set.</summary>
    private Color EffectiveBackColor => CollectionVisualAppearance.Field(this,
        Enabled ? BackColor : SystemColors.Control);

    private Color EffectiveForeColor => CollectionVisualAppearance.Text(this, ForeColor);

    /// <summary>The native control's painting: the background, the lines, the glyphs and every visible row's label.</summary>
    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        using (var background = new SolidBrush(EffectiveBackColor))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        if (_nodes.Count == 0) return;

        var rowTop = 0;
        var client = ClientSize;

        foreach (var node in VisibleRows())
        {
            if (rowTop >= client.Height) break;

            PaintRow(graphics, node, rowTop);
            rowTop += ItemHeight;
        }
    }

    /// <summary>The nodes shown from TopNode downward, as many as fit (and one extra, partly clipped).</summary>
    private IEnumerable<TreeNode> VisibleRows()
    {
        var topIndex = ExpandedOrderIndex(EffectiveTopNode()!);
        if (topIndex < 0) yield break;

        var rows = VisibleCount + 1;
        var index = 0;
        foreach (var node in EnumerateExpandedOrder())
        {
            if (index >= topIndex) yield return node;
            index++;
            if (index - topIndex >= rows) yield break;
        }
    }

    private void PaintRow(Graphics graphics, TreeNode node, int rowTop)
    {
        var layout = RowLayout(node, rowTop);

        if (DrawMode == TreeViewDrawMode.OwnerDrawAll)
        {
            if (!RaiseDrawNode(graphics, node, layout.Row)) return;
        }

        if (_fullRowSelect && node.IsSelected && (Focused || !_hideSelection))
        {
            using var selectionBrush = new SolidBrush(SelectionBackColor);
            graphics.FillRectangle(selectionBrush, layout.Row);
        }

        if (ShowLines) PaintLines(graphics, node, layout.Row, layout.PlusMinus);
        if (ShowPlusMinus && node.Nodes.Count > 0 && (node.Level > 0 || ShowRootLines)) PaintPlusMinus(graphics, layout.PlusMinus, node.IsExpanded);

        if (!layout.CheckBox.IsEmpty) PaintStateGlyph(graphics, node, layout.CheckBox);

        if (!layout.Image.IsEmpty && _imageList is { } images)
        {
            var index = EffectiveImageIndex(node);
            if (index >= 0 && index < images.Images.Count) images.Draw(graphics, layout.Image.Location, index);
        }

        PaintLabel(graphics, node, layout.Row, layout.Label);
    }

    /// <summary>A picture from StateImageList when the node names one, else the plain Windows check box.</summary>
    private void PaintStateGlyph(Graphics graphics, TreeNode node, Rectangle bounds)
    {
        var index = node.ResolveStateImageIndex(_stateImageList);
        if (_stateImageList is { } states && index >= 0 && index < states.Images.Count)
        {
            states.Draw(graphics, bounds.Location, index);
            return;
        }

        if (PortableVisualStylesEnabled)
        {
            PortableVisualStyles.PaintGlyph(graphics, bounds, CollectionVisualAppearance.Palette(this),
                radio: false, selected: node.Checked, mixed: false, pressed: false, enabled: Enabled);
        }
        else
        {
            ControlPaint.DrawCheckBox(graphics, bounds, node.Checked ? ButtonState.Checked : ButtonState.Normal);
        }
    }

    /// <summary>The node's own picture when it has one, else the tree view's default.</summary>
    private int EffectiveImageIndex(TreeNode node)
    {
        var selected = node.ResolveSelectedImageIndex(_imageList);
        if (node.IsSelected && selected >= 0) return selected;

        var own = node.ResolveImageIndex(_imageList);
        if (own >= 0) return own;

        if (node.IsSelected && ResolveSelectedImageIndex() >= 0) return ResolveSelectedImageIndex();

        return ResolveImageIndex();
    }

    /// <summary>A dotted vertical line down the node's own level, and a short horizontal stub into its glyph.</summary>
    private void PaintLines(Graphics graphics, TreeNode node, Rectangle row, Rectangle plusMinus)
    {
        if (node.Level == 0 && !ShowRootLines) return;

        var lineColor = PortableVisualStylesEnabled && !_lineColorExplicit
            ? CollectionVisualAppearance.Palette(this).Border : _lineColor;
        using var pen = new Pen(lineColor) { DashStyle = DashStyleOf() };

        var centerY = row.Top + row.Height / 2;
        var centerX = plusMinus.Left + plusMinus.Width / 2;

        graphics.DrawLine(pen, centerX, row.Top, centerX, row.Bottom);
        graphics.DrawLine(pen, centerX, centerY,
            _rightToLeftLayout ? plusMinus.Right : plusMinus.Left - 1, centerY);
    }

    private static DashStyle DashStyleOf() => DashStyle.Dot;

    private void PaintPlusMinus(Graphics graphics, Rectangle bounds, bool expanded)
    {
        var palette = PortableVisualStylesEnabled ? CollectionVisualAppearance.Palette(this) : null;
        using var border = new Pen(palette?.Border ?? SystemColors.ControlDark);
        using var fill = new SolidBrush(palette?.Field ?? SystemColors.Window);
        using var sign = new Pen(Enabled ? palette?.Text ?? SystemColors.WindowText
            : palette?.DisabledText ?? SystemColors.WindowText);

        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        var centerY = bounds.Top + bounds.Height / 2;
        var centerX = bounds.Left + bounds.Width / 2;
        graphics.DrawLine(sign, bounds.Left + 2, centerY, bounds.Right - 3, centerY);
        if (!expanded) graphics.DrawLine(sign, centerX, bounds.Top + 2, centerX, bounds.Bottom - 3);
    }

    private void PaintLabel(Graphics graphics, TreeNode node, Rectangle row, Rectangle label)
    {
        var selected = node.IsSelected;
        var showHighlight = selected && (Focused || !_hideSelection);

        var backColor = showHighlight ? SelectionBackColor : EffectiveBackColor;
        var foreColor = showHighlight ? SelectionForeColor
            : !Enabled && PortableVisualStylesEnabled ? CollectionVisualAppearance.Palette(this).DisabledText
            : node.ForeColor.IsEmpty ? EffectiveForeColor : node.ForeColor;

        var highlight = _fullRowSelect ? row : label;

        if (showHighlight && !_fullRowSelect)
        {
            using var highlightBrush = new SolidBrush(backColor);
            graphics.FillRectangle(highlightBrush, highlight);
        }
        else if (!node.BackColor.IsEmpty)
        {
            using var nodeBrush = new SolidBrush(node.BackColor);
            graphics.FillRectangle(nodeBrush, label);
        }

        if (node == _editingNode)
        {
            PaintLabelEdit(graphics, label);
            return;
        }

        if (DrawMode == TreeViewDrawMode.OwnerDrawText && !RaiseDrawNode(graphics, node, label)) return;

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            (_rightToLeftLayout ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(graphics, node.Text, node.NodeFont ?? Font, label, foreColor, flags);

        if (HotTracking && node == _hoverNode)
        {
            using var underline = new Pen(foreColor);
            var textWidth = TextRenderer.MeasureText(node.Text, Font).Width;
            graphics.DrawLine(underline, label.Left, label.Bottom - 1, label.Left + textWidth, label.Bottom - 1);
        }

        if (selected && Focused) ControlPaint.DrawFocusRectangle(graphics, highlight);
    }
}
