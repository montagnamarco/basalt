using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal.Text;

namespace System.Windows.Forms;

/// <summary>
/// The text of a ComboBox: for <see cref="ComboBoxStyle.DropDown"/> and
/// <see cref="ComboBoxStyle.Simple"/> it is a real edit with a caret and a
/// selection; for <see cref="ComboBoxStyle.DropDownList"/> it is read-only
/// and shows the selected item's text highlighted when focused.
/// </summary>
/// <remarks>
/// Reuses the same <see cref="TextEditor"/>/<see cref="TextView"/> pair the
/// text boxes use, kept directly on this control rather than through a child
/// control: there is only ever one line, so the layout here is a trimmed
/// version of what TextBoxBase does for a single line.
/// </remarks>
public partial class ComboBox
{
    private const int EditTextInset = 2;

    private readonly TextEditor _editor = new();
    private readonly TextView _view = new();
    private bool _viewDirty = true;
    private float _scrollX;
    private bool _selectingWithMouse;

    /// <summary>Set right before every OnTextChanged; read by the AutoComplete override in ComboBox.Hidden.cs.</summary>
    private bool _textChangedByUser;

    [Localizable(true)]
    [Bindable(true)]
    [AllowNull]
    public override string Text
    {
        get => _dropDownStyle == ComboBoxStyle.DropDownList && _selectedIndex < 0 ? string.Empty : _editor.Text;
        set
        {
            value ??= string.Empty;

            if (_dropDownStyle == ComboBoxStyle.DropDownList)
            {
                // A DropDownList combo only accepts text that matches an item exactly, as WinForms does.
                var index = FindStringExact(value);
                if (index >= 0) SetSelectedIndexCore(index);
                return;
            }

            if (value == _editor.Text) return;

            _editor.SetText(value);
            _scrollX = 0;
            InvalidateEdit();
            SyncSelectedIndexFromText();
            _textChangedByUser = false;
            OnTextChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    public string SelectedText
    {
        get => _editor.SelectedText;
        set
        {
            ReplaceSelectionWith(value ?? string.Empty, userAction: false);
        }
    }

    [Browsable(false)]
    public int SelectionStart
    {
        get => _editor.SelectionStart;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _editor.Select(value, SelectionLength);
            InvalidateEdit();
        }
    }

    [Browsable(false)]
    public int SelectionLength
    {
        get => _editor.SelectionLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _editor.Select(SelectionStart, value);
            InvalidateEdit();
        }
    }

    /// <summary>The underlying text storage, shared with the caret and the painting below.</summary>
    private TextEditor Editor => _editor;

    private bool IsEditable => _dropDownStyle != ComboBoxStyle.DropDownList;

    /// <summary>Where the item for the current text is, applied whenever the user edits the text of an editable combo.</summary>
    private void SyncSelectedIndexFromText()
    {
        var index = FindStringExact(_editor.Text);
        if (index == _selectedIndex) return;

        _selectedIndex = index;
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>Puts the selected item's text into the editor and raises TextChanged when that changed it.</summary>
    private void SyncTextWithSelection()
    {
        if (!SetTextFromSelection()) return;

        _textChangedByUser = false;
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Puts the selected item's text into the editor; true when the text changed, for the caller to report.</summary>
    private bool SetTextFromSelection()
    {
        var text = _selectedIndex >= 0 ? GetItemText(_items[_selectedIndex]) : string.Empty;
        if (text == _editor.Text) return false;

        _editor.SetText(text);
        _scrollX = 0;
        InvalidateEdit();
        return true;
    }

    private void ReplaceSelectionWith(string text, bool userAction)
    {
        if (!IsEditable) return;
        if (!_editor.ReplaceSelection(text)) return;

        TextEdited(userAction);
    }

    private void TextEdited(bool userAction)
    {
        InvalidateEdit();
        ScrollToCaret();
        SyncSelectedIndexFromText();

        // As CBN_EDITUPDATE comes before CBN_EDITCHANGE: TextUpdate first, then TextChanged.
        if (userAction) OnTextUpdate(EventArgs.Empty);

        // Read by the OnTextChanged override below, exactly as TextBox reads its own Modified: only a
        // keystroke (not code assigning Text, including this class's own inline completion) suggests.
        _textChangedByUser = userAction;
        OnTextChanged(EventArgs.Empty);
    }

    private void MoveCaret(bool extend, int position)
    {
        _editor.MoveCaret(position, extend);
        ResetCaretBlink();
        ScrollToCaret();
        InvalidateEdit();
    }

    // --- Layout --------------------------------------------------------------

    private const int DropButtonWidth = 16;

    /// <summary>The one-line height of the edit row: the whole client area, except for Simple, which keeps a line and gives the rest to the list.</summary>
    private int EditRowHeight => _dropDownStyle == ComboBoxStyle.Simple ? Math.Max(FontHeight + 4, 1) : ClientRectangle.Height;

    private Rectangle ButtonBounds => _dropDownStyle == ComboBoxStyle.Simple
        ? Rectangle.Empty
        : new Rectangle(ClientRectangle.Right - DropButtonWidth, ClientRectangle.Top, Math.Min(DropButtonWidth, ClientRectangle.Width), EditRowHeight);

    private Rectangle EditBounds => new(ClientRectangle.Left, ClientRectangle.Top, Math.Max(ClientRectangle.Width - ButtonBounds.Width, 0), EditRowHeight);

    private Rectangle TextArea()
    {
        var bounds = EditBounds;
        return Rectangle.FromLTRB(bounds.Left + EditTextInset, bounds.Top, bounds.Right - EditTextInset, bounds.Bottom);
    }

    // --- Painting --------------------------------------------------------------

    /// <summary>Draws the edit row: the selected item highlighted when read-only and focused, or real text with a caret when editable.</summary>
    private void PaintEditArea(Graphics graphics)
    {
        var bounds = EditBounds;
        var textArea = TextArea();
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;

        if (!IsEditable)
        {
            var highlighted = Focused;
            var readOnlyForeColor = highlighted ? CollectionVisualAppearance.SelectionText(this, Focused) : EffectiveForeColor;
            var readOnlyBackColor = highlighted ? CollectionVisualAppearance.SelectionField(this, Focused) : EffectiveBackColor;

            using (var brush = new SolidBrush(readOnlyBackColor)) graphics.FillRectangle(brush, bounds);

            TextRenderer.DrawText(graphics, _editor.Text, Font, textArea, readOnlyForeColor, flags);
            return;
        }

        EnsureView();

        var state = graphics.Save();
        graphics.IntersectClip(bounds);

        var foreColor = EffectiveForeColor;
        var showSelection = SelectionLength > 0 && Focused;
        var left = textArea.Left - _scrollX;

        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None;

        var top = textArea.Top + (textArea.Height - _view.LineHeight) / 2f;

        using (var textBrush = new SolidBrush(foreColor))
        {
            graphics.DrawString(_editor.Text, Font, textBrush, left, top, format);
        }

        if (showSelection && _view.Lines.Count > 0)
        {
            var line = _view.Lines[0];
            var x1 = left + line.X(SelectionStart);
            var x2 = left + line.X(SelectionStart + SelectionLength);

            using var selectionBrush = new SolidBrush(CollectionVisualAppearance.SelectionField(this, Focused));
            var highlight = RectangleF.FromLTRB(x1, top, x2, top + _view.LineHeight);
            graphics.FillRectangle(selectionBrush, highlight);

            var clipState = graphics.Save();
            graphics.IntersectClip(highlight);
            using var selectedTextBrush = new SolidBrush(CollectionVisualAppearance.SelectionText(this, Focused));
            graphics.DrawString(_editor.Text, Font, selectedTextBrush, left, top, format);
            graphics.Restore(clipState);
        }

        if (Focused && EditCaretBlinker.Visible && Enabled)
        {
            var line = _view.Lines.Count > 0 ? _view.Lines[0] : new TextView.Line(0, 0, [0f]);
            var x = (int)Math.Round(left + line.X(_editor.Caret));

            using var caretBrush = new SolidBrush(PortableVisualStylesEnabled ? EffectiveForeColor : SystemColors.WindowText);
            graphics.FillRectangle(caretBrush, x, (int)top, 1, _view.LineHeight);
        }

        graphics.Restore(state);
    }

    private void EnsureView()
    {
        if (!_viewDirty) return;

        _view.Layout(_editor.Text, Font, multiline: false, wrapWidth: 0, passwordChar: '\0');
        _viewDirty = false;
    }

    private void InvalidateEdit()
    {
        _viewDirty = true;
        Invalidate(EditBounds);
    }

    private void ScrollToCaret()
    {
        EnsureView();

        var area = TextArea();
        var caret = _view.PositionOf(_editor.Caret);

        if (caret.X - _scrollX > area.Width - 1) _scrollX = caret.X - area.Width + 1 + area.Width / 3f;
        if (caret.X - _scrollX < 0) _scrollX = Math.Max(caret.X - area.Width / 3f, 0);

        var widest = _view.Lines.Count > 0 ? _view.Lines[0].Width : 0;
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(widest - area.Width + 1, 0));
    }

    private int IndexAtPoint(Point point)
    {
        EnsureView();
        var area = TextArea();

        return _view.IndexAt(new PointF(point.X - area.X + _scrollX, 0));
    }

    private void ResetCaretBlink()
    {
        if (Focused) EditCaretBlinker.Restart();
    }

    /// <summary>One caret blinks at a time, in the focused combo box, at the system's rate.</summary>
    private static class EditCaretBlinker
    {
        private const int BlinkMilliseconds = 530;

        private static Timer? s_timer;
        private static ComboBox? s_owner;

        public static bool Visible { get; private set; } = true;

        public static void Start(ComboBox owner)
        {
            s_owner = owner;
            Visible = true;

            s_timer ??= CreateTimer();
            s_timer.Stop();
            s_timer.Start();
        }

        public static void Stop(ComboBox owner)
        {
            if (s_owner != owner) return;

            s_owner = null;
            s_timer?.Stop();
        }

        public static void Restart()
        {
            Visible = true;
            s_timer?.Stop();
            s_timer?.Start();
        }

        private static Timer CreateTimer()
        {
            var timer = new Timer { Interval = BlinkMilliseconds };
            timer.Tick += (_, _) =>
            {
                Visible = !Visible;

                if (s_owner is { IsDisposed: false } owner) owner.InvalidateEdit();
                else s_timer?.Stop();
            };

            return timer;
        }
    }

    // --- Default procedure: the edit's own mouse and keyboard -----------------

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanFocus) Focus();

        if (!IsEditable || !EditBounds.Contains(e.Location)) return;

        var index = IndexAtPoint(e.Location);

        if (e.Clicks >= 2)
        {
            var (start, end) = WordAt(index);
            _editor.Select(start, end - start);
        }
        else
        {
            _editor.MoveCaret(index, extend: (ModifierKeys & Keys.Shift) != 0);
            _selectingWithMouse = true;
        }

        ResetCaretBlink();
        ScrollToCaret();
        InvalidateEdit();
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_selectingWithMouse || (e.Button & MouseButtons.Left) == 0) return;

        _editor.MoveCaret(IndexAtPoint(e.Location), extend: true);
        ScrollToCaret();
        InvalidateEdit();
    }

    internal override void DefaultMouseUp(MouseEventArgs e) => _selectingWithMouse = false;

    internal override void DefaultCaptureLost() => _selectingWithMouse = false;

    internal override void DefaultGotFocus()
    {
        EditCaretBlinker.Start(this);
        Invalidate();
    }

    internal override void DefaultLostFocus()
    {
        EditCaretBlinker.Stop(this);
        Invalidate();
    }

    internal override bool DialogCodeWantsChar(char charCode) => IsEditable;

    /// <summary>The character keys left unhandled by the combo-specific OnKeyPress: typed text, for an editable combo.</summary>
    internal override void DefaultKeyPress(KeyPressEventArgs e)
    {
        if (!IsEditable) return;

        switch (e.KeyChar)
        {
            case '\b':
                if (_editor.Delete(backwards: true, wholeWord: false)) TextEdited(userAction: true);
                return;

            case '\r' or '\t':
                return;
        }

        if (char.IsControl(e.KeyChar)) return;

        ReplaceSelectionWith(e.KeyChar.ToString(), userAction: true);
    }

    /// <summary>The navigation and editing keys left unhandled by the combo-specific OnKeyDown: caret movement, for an editable combo.</summary>
    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (!IsEditable) return;

        EnsureView();
        var shift = e.Shift;
        var control = e.Control;

        switch (e.KeyCode)
        {
            case Keys.Left:
                MoveCaret(shift, control ? _editor.WordBoundary(_editor.Caret, forward: false) : CollapseOr(_editor.PreviousCharacter(_editor.Caret), SelectionStart, shift));
                e.Handled = true;
                return;

            case Keys.Right:
                MoveCaret(shift, control ? _editor.WordBoundary(_editor.Caret, forward: true) : CollapseOr(_editor.NextCharacter(_editor.Caret), SelectionStart + SelectionLength, shift));
                e.Handled = true;
                return;

            case Keys.Home:
                MoveCaret(shift, 0);
                e.Handled = true;
                return;

            case Keys.End:
                MoveCaret(shift, _editor.Length);
                e.Handled = true;
                return;

            case Keys.Delete:
                if (_editor.Delete(backwards: false, wholeWord: control)) TextEdited(userAction: true);
                e.Handled = true;
                return;
        }
    }

    private int CollapseOr(int moved, int edge, bool shift) => !shift && SelectionLength > 0 ? edge : moved;

    private (int Start, int End) WordAt(int position) => _editor.WordAt(position);
}
