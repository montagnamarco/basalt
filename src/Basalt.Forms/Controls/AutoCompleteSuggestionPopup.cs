using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// The borderless popup list <see cref="AutoCompleteMode.Suggest"/> and
/// <see cref="AutoCompleteMode.SuggestAppend"/> show under a TextBox's or an
/// editable ComboBox's edit area: every candidate from
/// <see cref="AutoCompleteSource"/>/<see cref="TextBox.AutoCompleteCustomSource"/>
/// that starts with what the user has typed so far.
/// </summary>
/// <remarks>
/// Painted and hit-tested the same way <see cref="ComboBox"/>'s own
/// drop-down list is (see ComboBox.DropDown.cs): the owner keeps the focus
/// and the caret throughout, so typing never stops to let the popup take it.
/// </remarks>
internal sealed class AutoCompleteSuggestionPopup : Control
{
    private readonly Control _owner;
    private readonly Action<string> _onAccept;
    private IReadOnlyList<string> _matches = [];
    private int _highlighted = -1;

    public AutoCompleteSuggestionPopup(Control owner, Action<string> onAccept)
    {
        _owner = owner;
        _onAccept = onAccept;
        SetTopLevel(true);
        SetStyle(ControlStyles.UserPaint, false);
    }

    internal override bool TakesKeyboardWhileOpen => false;

    /// <summary>Whether the popup is currently open: the owner checks this before Up/Down/Enter/Escape try to act on it.</summary>
    public bool IsOpen => Popups.All.Contains(this);

    private int RowHeight => Math.Max(Font.Height + 2, 1);

    /// <summary>Shows (or moves) the popup under <paramref name="anchorScreenBounds"/>, listing every match.</summary>
    public void ShowMatches(IReadOnlyList<string> matches, Rectangle anchorScreenBounds)
    {
        _matches = matches;
        _highlighted = -1;
        Font = _owner.Font;

        const int maxVisibleRows = 8;
        var rows = Math.Max(Math.Min(matches.Count, maxVisibleRows), 1);
        var bounds = new Rectangle(anchorScreenBounds.X, anchorScreenBounds.Bottom, anchorScreenBounds.Width, rows * RowHeight);

        Popups.Show(this, _owner, bounds);
        Invalidate();
    }

    public void HideSuggestions() => Popups.Close(this);

    /// <summary>Up (-1) or Down (+1) through the list, wrapping from -1 (none) to the first or last row, as Windows' own suggestion list does.</summary>
    public void MoveHighlight(int delta)
    {
        if (_matches.Count == 0) return;

        var next = _highlighted + delta;
        if (next < 0) next = _matches.Count - 1;
        else if (next >= _matches.Count) next = 0;

        _highlighted = next;
        Invalidate();
    }

    /// <summary>Accepts the highlighted row, if any; false (and does nothing) when nothing is highlighted, so Enter with no highlight falls through to the owner's own handling.</summary>
    public bool AcceptHighlighted()
    {
        if (_highlighted < 0 || _highlighted >= _matches.Count) return false;

        _onAccept(_matches[_highlighted]);
        HideSuggestions();
        return true;
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using (var back = new SolidBrush(SystemColors.Window)) graphics.FillRectangle(back, ClientRectangle);

        var rowHeight = RowHeight;
        for (var i = 0; i < _matches.Count; i++)
        {
            var bounds = new Rectangle(0, i * rowHeight, ClientRectangle.Width, rowHeight);
            if (bounds.Top >= ClientRectangle.Height) break;

            var highlighted = i == _highlighted;
            var backColor = highlighted ? SystemColors.Highlight : SystemColors.Window;
            var foreColor = highlighted ? SystemColors.HighlightText : SystemColors.WindowText;

            using (var rowBrush = new SolidBrush(backColor)) graphics.FillRectangle(rowBrush, bounds);

            var textBounds = new Rectangle(bounds.X + 2, bounds.Y, Math.Max(bounds.Width - 4, 0), bounds.Height);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(graphics, _matches[i], Font, textBounds, foreColor, flags);
        }
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index < 0 || index == _highlighted) return;

        _highlighted = index;
        Invalidate();
    }

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var index = IndexAt(e.Location);
        if (index < 0) return;

        _onAccept(_matches[index]);
        HideSuggestions();
    }

    private int IndexAt(Point point)
    {
        if (point.Y < 0) return -1;

        var index = point.Y / RowHeight;
        return index >= 0 && index < _matches.Count ? index : -1;
    }
}
