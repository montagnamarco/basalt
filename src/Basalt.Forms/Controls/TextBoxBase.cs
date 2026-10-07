using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;
using System.Windows.Forms.Internal.Text;

namespace System.Windows.Forms;

/// <summary>
/// What TextBox and the other edit controls share: the text with its
/// selection, editing by keyboard and mouse, the clipboard, a single level
/// of undo, scrolling to the caret, and painting in the classic look.
/// </summary>
/// <remarks>
/// Windows' EDIT control does this natively; here it is ours, with its
/// rules: the user's KeyDown and KeyPress handlers run first and can stop
/// the default editing, setting Text clears the undo and puts the caret at
/// the start, and the text area is inset by one pixel from the frame.
/// </remarks>
[DefaultEvent("TextChanged")]
public abstract partial class TextBoxBase : Control
{
    private const int TextInset = 1;

    private readonly TextEditor _editor = new();
    private readonly TextView _view = new();
    private bool _viewDirty = true;

    private BorderStyle _borderStyle = BorderStyle.Fixed3D;
    private bool _multiline;
    private bool _wordWrap = true;
    private bool _readOnly;
    private bool _acceptsTab;
    private bool _hideSelection = true;
    private bool _modified;
    private bool _shortcutsEnabled = true;

    private float _scrollX;
    private float _scrollY;
    private bool _selectingWithMouse;

    private protected TextBoxBase()
    {
        SetStyle(ControlStyles.FixedHeight, true);
        // The EDIT control paints and handles the mouse itself: no UserPaint, no standard clicks.
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.UseTextForAccessibility | ControlStyles.UserPaint, false);
        base.AutoSize = true;
    }

    protected override Size DefaultSize => new(100, PreferredHeight);

    protected override Cursor DefaultCursor => Cursors.IBeam;

    /// <summary>The height of one line with the frame: the font's, plus the border's four lines and three pixels, as WinForms computes it.</summary>
    [Browsable(false)]
    public int PreferredHeight
    {
        get
        {
            var height = FontHeight;
            if (_borderStyle != BorderStyle.None) height += NonClientArea.BorderWidth * 4 + 3;

            return height;
        }
    }

    [DefaultValue(true)]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set
        {
            if (value == base.AutoSize) return;

            base.AutoSize = value;
            if (!_multiline) AdjustHeight();
        }
    }

    [DefaultValue(BorderStyle.Fixed3D)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
            AdjustHeight();
            OnBorderStyleChanged(EventArgs.Empty);
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;
            else if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;

            return parameters;
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.All)]
    public virtual bool Multiline
    {
        get => _multiline;
        set
        {
            if (value == _multiline) return;

            _multiline = value;
            SetStyle(ControlStyles.FixedHeight, !value);
            InvalidateView();
            AdjustHeight();
            OnMultilineChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    [Localizable(true)]
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (value == _wordWrap) return;

            _wordWrap = value;
            InvalidateView();
        }
    }

    [DefaultValue(false)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (value == _readOnly) return;

            _readOnly = value;
            Invalidate();
            OnReadOnlyChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public bool AcceptsTab
    {
        get => _acceptsTab;
        set
        {
            if (value == _acceptsTab) return;

            _acceptsTab = value;
            OnAcceptsTabChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool HideSelection
    {
        get => _hideSelection;
        set
        {
            if (value == _hideSelection) return;

            _hideSelection = value;
            Invalidate();
            OnHideSelectionChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public virtual bool ShortcutsEnabled
    {
        get => _shortcutsEnabled;
        set => _shortcutsEnabled = value;
    }

    [DefaultValue(32767)]
    [Localizable(true)]
    public virtual int MaxLength
    {
        get => _editor.MaxLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _editor.MaxLength = value;
        }
    }

    /// <summary>Whether the user changed the text since it was set or the flag was cleared.</summary>
    [Browsable(false)]
    public bool Modified
    {
        get => _modified;
        set
        {
            if (value == _modified) return;

            _modified = value;
            OnModifiedChanged(EventArgs.Empty);
        }
    }

    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => _editor.Text;
        set
        {
            value ??= string.Empty;
            if (value == _editor.Text) return;

            _editor.SetText(value);
            _scrollX = 0;
            _scrollY = 0;
            InvalidateView();
            Modified = false;
            OnTextChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    public virtual int TextLength => _editor.Length;

    /// <summary>The text's lines, broken at CR LF, LF or CR; setting joins them with CR LF.</summary>
    [Localizable(true)]
    [AllowNull]
    public string[] Lines
    {
        get => _editor.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        set => Text = value is null ? string.Empty : string.Join("\r\n", value);
    }

    [Browsable(false)]
    public bool CanUndo => CanUndoCore;

    [Browsable(false)]
    [AllowNull]
    public virtual string SelectedText
    {
        get => _editor.SelectedText;
        set
        {
            ReplaceSelectionWith(value ?? string.Empty, userAction: false);
            _editor.ClearUndo();
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
            SelectionChanged();
        }
    }

    [Browsable(false)]
    public virtual int SelectionLength
    {
        get => _editor.SelectionLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _editor.Select(SelectionStart, value);
            SelectionChanged();
        }
    }

    public void Select(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);

        // A negative length selects backwards from start, as the EDIT control allows.
        if (length < 0)
        {
            var end = Math.Max(start + length, 0);
            _editor.Select(start, 0);
            _editor.MoveCaret(end, extend: true);
        }
        else
        {
            _editor.Select(start, length);
        }

        SelectionChanged();
    }

    public void SelectAll()
    {
        _editor.Select(0, _editor.Length);
        SelectionChanged();
    }

    public void DeselectAll()
    {
        _editor.Select(SelectionStart, 0);
        SelectionChanged();
    }

    public void AppendText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        AppendTextCore(text);
    }

    public void Clear() => Text = null;

    public void ClearUndo() => _editor.ClearUndo();

    public void Undo() => UndoCore();

    public void Copy() => CopyCore();

    public void Cut() => CutCore();

    public void Paste() => PasteCore();

    // The editing commands are not virtual in WinForms: a masked box answers
    // the WM_CUT/WM_PASTE/WM_UNDO messages they send. These hooks stand in for
    // that, so a call from code or a menu goes through the mask like a keystroke.

    internal virtual bool CanUndoCore => _editor.CanUndo;

    internal virtual void AppendTextCore(string text)
    {
        _editor.Select(_editor.Length, 0);
        ReplaceSelectionWith(text, userAction: false);
        ScrollToCaret();
    }

    internal virtual void UndoCore()
    {
        if (!_editor.Undo()) return;

        TextEdited();
    }

    internal virtual void CopyCore()
    {
        if (SelectionLength > 0 && !IsPassword) PlatformClipboard.SetText(SelectedText);
    }

    internal virtual void CutCore()
    {
        if (SelectionLength == 0 || _readOnly || IsPassword) return;

        PlatformClipboard.SetText(SelectedText);

        // Waiting for the clipboard runs the message loop, which may have closed the form.
        if (IsDisposed) return;

        ReplaceSelectionWith(string.Empty, userAction: true);
    }

    internal virtual void PasteCore()
    {
        if (_readOnly) return;

        var text = PlatformClipboard.GetText();
        if (string.IsNullOrEmpty(text) || IsDisposed) return;

        ReplaceSelectionWith(PrepareInput(text), userAction: true);
    }

    /// <summary>Scrolls so that the caret is in view.</summary>
    public void ScrollToCaret()
    {
        EnsureView();

        var area = TextArea();
        var caret = _view.PositionOf(_editor.Caret);

        if (caret.X - _scrollX > area.Width - 1) _scrollX = caret.X - area.Width + 1 + area.Width / 3f;
        if (caret.X - _scrollX < 0) _scrollX = Math.Max(caret.X - area.Width / 3f, 0);
        if (!_multiline || !_wordWrap)
        {
            var widest = _view.Lines.Count > 0 ? _view.Lines.Max(line => line.Width) : 0;
            _scrollX = Math.Clamp(_scrollX, 0, Math.Max(widest - area.Width + 1, 0));
        }

        if (_multiline)
        {
            if (caret.Y - _scrollY + _view.LineHeight > area.Height) _scrollY = caret.Y + _view.LineHeight - area.Height;
            if (caret.Y - _scrollY < 0) _scrollY = caret.Y;
        }

        Invalidate();
    }

    public virtual int GetCharIndexFromPosition(Point pt)
    {
        EnsureView();
        var area = TextArea();
        var index = _view.IndexAt(new PointF(pt.X - area.X + _scrollX, pt.Y - area.Y + _scrollY));

        // The EDIT control answers the last character, not the position after it.
        return Math.Min(index, Math.Max(_editor.Length - 1, 0));
    }

    public virtual char GetCharFromPosition(Point pt)
    {
        var text = _editor.Text;

        return text.Length == 0 ? '\0' : text[GetCharIndexFromPosition(pt)];
    }

    public virtual Point GetPositionFromCharIndex(int index)
    {
        if (index < 0 || index >= _editor.Length) return Point.Empty;

        EnsureView();
        var area = TextArea();
        var position = _view.PositionOf(index);

        return new Point((int)(position.X - _scrollX) + area.X, (int)(position.Y - _scrollY) + area.Y);
    }

    public virtual int GetLineFromCharIndex(int index)
    {
        EnsureView();

        return _view.LineOf(index);
    }

    public int GetFirstCharIndexFromLine(int lineNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);

        return FirstCharIndexFromLineCore(lineNumber);
    }

    public int GetFirstCharIndexOfCurrentLine() => FirstCharIndexOfCurrentLineCore(_editor.Caret);

    // Not virtual in WinForms, where the native control answers EM_LINEINDEX; a box with its own
    // layout (RichTextBox's formatted lines) answers from that layout through these.

    internal virtual int FirstCharIndexFromLineCore(int lineNumber)
    {
        EnsureView();

        return lineNumber < _view.Lines.Count ? _view.Lines[lineNumber].Start : -1;
    }

    /// <summary>The start of the caret's line: the caret, not SelectionStart, which is the other end of a forward selection.</summary>
    internal virtual int FirstCharIndexOfCurrentLineCore(int caret)
    {
        EnsureView();

        return _view.Lines.Count == 0 ? 0 : _view.Lines[_view.LineOf(caret)].Start;
    }

    /// <summary>Whether the keys move around the text, which a text box wants and a dialog does not get.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) == Keys.Alt) return false;

        switch (keyData & Keys.KeyCode)
        {
            case Keys.Tab:
                return _multiline && _acceptsTab && (keyData & Keys.Control) == Keys.None;

            case Keys.Escape:
                return false;

            case Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Left or Keys.Right or Keys.Up or Keys.Down:
                return true;
        }

        return base.IsInputKey(keyData);
    }

    internal override bool DialogCodeWantsChar(char charCode) => true;

    /// <summary>Ctrl+A selects everything, before the form sees it as a shortcut.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_shortcutsEnabled && keyData == (Keys.Control | Keys.A))
        {
            SelectAll();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Ctrl+Tab in a text box that takes Tab still moves to the next control.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && _acceptsTab && (keyData & Keys.Control) != 0)
        {
            keyData &= ~Keys.Control;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>What the handlers left unhandled is the edit control's own processing.</summary>
    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (HandleEditingKey(e.KeyCode, e.Shift, e.Control)) e.Handled = true;
    }

    internal override void DefaultKeyPress(KeyPressEventArgs e) => HandleCharacter(e.KeyChar);

    private bool HandleEditingKey(Keys key, bool shift, bool control)
    {
        EnsureView();

        switch (key)
        {
            case Keys.Left:
                MoveCaret(shift, control ? _editor.WordBoundary(_editor.Caret, forward: false) : CollapseOr(_editor.PreviousCharacter(_editor.Caret), SelectionStart, shift));
                return true;

            case Keys.Right:
                MoveCaret(shift, control ? _editor.WordBoundary(_editor.Caret, forward: true) : CollapseOr(_editor.NextCharacter(_editor.Caret), SelectionStart + SelectionLength, shift));
                return true;

            case Keys.Home:
                MoveCaret(shift, control ? 0 : _view.Lines[_view.LineOf(_editor.Caret)].Start);
                return true;

            case Keys.End:
                MoveCaret(shift, control ? _editor.Length : _view.Lines[_view.LineOf(_editor.Caret)].End);
                return true;

            case Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown when _multiline:
            {
                var lines = key is Keys.PageUp or Keys.PageDown ? Math.Max(TextArea().Height / Math.Max(_view.LineHeight, 1) - 1, 1) : 1;
                var direction = key is Keys.Up or Keys.PageUp ? -1 : 1;
                var current = _view.LineOf(_editor.Caret);
                var target = Math.Clamp(current + direction * lines, 0, _view.Lines.Count - 1);
                MoveCaret(shift, _view.IndexInLine(target, _view.PositionOf(_editor.Caret).X));
                return true;
            }

            case Keys.Delete when shift && !control:
                if (_shortcutsEnabled) Cut();
                return true;

            case Keys.Delete:
                if (_readOnly) return true;

                if (_editor.Delete(backwards: false, wholeWord: control)) TextEdited();
                return true;

            case Keys.Insert when control && _shortcutsEnabled:
                Copy();
                return true;

            case Keys.Insert when shift && _shortcutsEnabled:
                Paste();
                return true;

            case Keys.C when control && _shortcutsEnabled:
                Copy();
                return true;

            case Keys.X when control && _shortcutsEnabled:
                Cut();
                return true;

            case Keys.V when control && _shortcutsEnabled:
                Paste();
                return true;

            case Keys.Z when control && _shortcutsEnabled:
                Undo();
                return true;
        }

        return false;
    }

    /// <summary>Left or Right on a selection collapses it to its edge instead of moving.</summary>
    private int CollapseOr(int moved, int edge, bool shift) => !shift && SelectionLength > 0 ? edge : moved;

    private void HandleCharacter(char character)
    {
        if (_readOnly) return;

        switch (character)
        {
            case '\b':
                if (_editor.Delete(backwards: true, wholeWord: false)) TextEdited();
                return;

            case '\r':
                if (_multiline && AcceptsReturnInternal) ReplaceSelectionWith("\r\n", userAction: true);
                return;

            case '\t':
                if (_multiline && _acceptsTab) ReplaceSelectionWith("\t", userAction: true);
                return;
        }

        // Other control characters (Ctrl+letter) were shortcuts, not text.
        if (char.IsControl(character)) return;

        ReplaceSelectionWith(PrepareInput(character.ToString()), userAction: true);
    }

    /// <summary>Whether Enter makes a new line; TextBox decides with AcceptsReturn.</summary>
    private protected virtual bool AcceptsReturnInternal => true;

    /// <summary>Typed or pasted text as the control stores it: TextBox changes its case or keeps one line.</summary>
    private protected virtual string PrepareInput(string text) => _multiline ? text : FirstLine(text);

    private protected static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);

        return end < 0 ? text : text[..end];
    }

    /// <summary>Replaces the selection keeping the undo, as TextBox.Paste(string) does.</summary>
    private protected void PasteText(string text) => ReplaceSelectionWith(PrepareInput(text), userAction: true);

    private void ReplaceSelectionWith(string text, bool userAction)
    {
        if (!_editor.ReplaceSelection(text)) return;

        TextEdited(userAction);
    }

    private void TextEdited(bool userAction = true)
    {
        InvalidateView();
        ScrollToCaret();
        if (userAction) Modified = true;
        OnTextChanged(EventArgs.Empty);
    }

    private void MoveCaret(bool extend, int position)
    {
        _editor.MoveCaret(position, extend);
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        ResetCaretBlink();
        ScrollToCaret();
        SelectionChangedInternal();
    }

    /// <summary>After any change of selection or caret, from code or the user: RichTextBox raises SelectionChanged here.</summary>
    internal virtual void SelectionChangedInternal()
    {
    }

    /// <summary>The edit control takes the focus and places the caret before OnMouseDown runs.</summary>
    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (CanSelect) Focus();

        EnsureView();
        var index = IndexAtPoint(e.Location);

        if (e.Clicks >= 2)
        {
            var (start, end) = _editor.WordAt(index);
            _editor.Select(start, end - start);
        }
        else
        {
            _editor.MoveCaret(index, extend: (ModifierKeys & Keys.Shift) != 0);
            _selectingWithMouse = true;
        }

        SelectionChanged();
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_selectingWithMouse || (e.Button & MouseButtons.Left) == 0) return;

        _editor.MoveCaret(IndexAtPoint(e.Location), extend: true);
        SelectionChanged();
    }

    internal override void DefaultMouseUp(MouseEventArgs e) => _selectingWithMouse = false;

    private int IndexAtPoint(Point point)
    {
        var area = TextArea();

        return _view.IndexAt(new PointF(point.X - area.X + _scrollX, point.Y - area.Y + _scrollY));
    }

    internal override void DefaultGotFocus()
    {
        CaretBlinker.Start(this);
        Invalidate();
    }

    internal override void DefaultLostFocus()
    {
        CaretBlinker.Stop(this);
        Invalidate();
    }

    /// <summary>A box destroyed while focused (its form closed) stops the caret that blinks for it.</summary>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        CaretBlinker.Stop(this);
        base.OnHandleDestroyed(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        InvalidateView();
        AdjustHeight();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        InvalidateView();
    }

    /// <summary>A single-line text box that sizes itself is exactly one line high.</summary>
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (!_multiline && AutoSize) height = PreferredHeight;

        base.SetBoundsCore(x, y, width, height, specified);
    }

    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var frame = NonClient;
        var text = TextRenderer.MeasureText(_editor.Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);

        return new Size(text.Width + frame.Horizontal + 2 * TextInset + Padding.Horizontal, PreferredHeight);
    }

    private void AdjustHeight()
    {
        if (_multiline || !AutoSize) return;

        if (Height != PreferredHeight) Height = PreferredHeight;
    }

    /// <summary>Where the text is drawn: the client area less the inset and the padding.</summary>
    private Rectangle TextArea()
    {
        var client = ClientRectangle;

        return Rectangle.FromLTRB(
            client.Left + TextInset + Padding.Left,
            client.Top + TextInset + Padding.Top,
            client.Right - TextInset - Padding.Right,
            client.Bottom - Padding.Bottom);
    }

    private void InvalidateView()
    {
        _viewDirty = true;
        Invalidate();
    }

    private int _laidOutWidth = -1;

    private void EnsureView()
    {
        var wrapWidth = _multiline && _wordWrap ? Math.Max(TextArea().Width, 1) : 0;

        // A wrapping box lays out again when its width changes.
        if (!_viewDirty && wrapWidth == _laidOutWidth) return;

        _laidOutWidth = wrapWidth;
        _view.Layout(_editor.Text, Font, _multiline, wrapWidth, DisplayPasswordChar);
        _viewDirty = false;
    }

    private protected virtual char DisplayPasswordChar => '\0';

    private protected bool IsPassword => DisplayPasswordChar != '\0';

    /// <summary>The text to show greyed when the box is empty and not focused.</summary>
    private protected virtual string PlaceholderTextInternal => string.Empty;

    /// <summary>Where a line starts horizontally: TextBox aligns right or centre.</summary>
    private protected virtual float AlignmentOffset(float lineWidth, int areaWidth) => 0;



    /// <summary>
    /// Visual Studio shows an unset BackColor as "Window", the usual edit box
    /// shade, rather than the generic control colour every other control
    /// declares. Painting still goes through <see cref="EffectiveBackColor"/>,
    /// which additionally greys out a disabled or read-only box.
    /// </summary>
    internal override Color DeclaredDefaultBackColor => SystemColors.Window;

    /// <summary>Window white normally; the control colour when disabled or read-only, unless a colour was set.</summary>
    private Color EffectiveBackColor
    {
        get
        {
            if (ShouldSerializeBackColor()) return BackColor;

            if (PortableVisualStylesEnabled)
            {
                var palette = Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform);
                return Enabled && !_readOnly ? palette.Field : palette.Surface;
            }

            return Enabled && !_readOnly ? SystemColors.Window : SystemColors.Control;
        }
    }

    /// <summary>The EDIT control's painting: its background, the text, the selection and the caret.</summary>
    internal override void DefaultPaint(PaintEventArgs e)
    {
        EnsureView();

        var graphics = e.Graphics;

        using (var background = new SolidBrush(EffectiveBackColor))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        var area = TextArea();
        var state = graphics.Save();
        graphics.IntersectClip(Rectangle.FromLTRB(area.Left, area.Top, area.Right, ClientRectangle.Bottom));

        if (_editor.Length == 0 && !Focused && PlaceholderTextInternal.Length > 0)
        {
            TextRenderer.DrawText(graphics, PlaceholderTextInternal, Font, area, SystemColors.GrayText, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        PaintLines(graphics, area);
        graphics.Restore(state);
    }

    private void PaintLines(Graphics graphics, Rectangle area)
    {
        var display = DisplayPasswordChar == '\0' ? _editor.Text : new string(DisplayPasswordChar, _editor.Length);
        var palette = PortableVisualStylesEnabled
            ? Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
        var foreColor = Enabled ? (HasExplicitAppearanceForeColor ? ForeColor : palette?.Text ?? SystemColors.WindowText)
            : palette?.DisabledText ?? SystemColors.GrayText;
        var showSelection = SelectionLength > 0 && (Focused || !_hideSelection);
        var selectionBack = Focused ? palette?.Selection ?? SystemColors.Highlight : palette?.HotFace ?? SystemColors.Control;
        var selectionFore = Focused ? palette?.SelectedText ?? SystemColors.HighlightText : palette?.Text ?? SystemColors.WindowText;

        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None;

        using var textBrush = new SolidBrush(foreColor);
        using var selectionBrush = new SolidBrush(selectionBack);
        using var selectedTextBrush = new SolidBrush(selectionFore);

        var selectionStart = SelectionStart;
        var selectionEnd = SelectionStart + SelectionLength;

        for (var i = 0; i < _view.Lines.Count; i++)
        {
            var line = _view.Lines[i];
            var top = area.Top + i * _view.LineHeight - _scrollY;
            if (top + _view.LineHeight < area.Top || top > area.Bottom) continue;

            var left = area.Left - _scrollX + AlignmentOffset(line.Width, area.Width);
            var text = display.Substring(line.Start, line.Length);

            graphics.DrawString(text, Font, textBrush, left, top, format);

            if (!showSelection) continue;

            var from = Math.Max(selectionStart, line.Start);
            var to = Math.Min(selectionEnd, line.End);
            if (from >= to && !(selectionStart <= line.End && selectionEnd > line.End && i + 1 < _view.Lines.Count)) continue;

            var x1 = left + line.X(from);
            var x2 = left + line.X(Math.Max(to, from));

            // A selected line break shows as a little block, as in Windows; a wrap point has no break to show.
            var hardBreak = i + 1 < _view.Lines.Count && _view.Lines[i + 1].Start > line.End;
            if (selectionEnd > line.End && hardBreak) x2 += AdvanceWidths.Of(Font, ' ');

            var highlight = RectangleF.FromLTRB(x1, top, x2, top + _view.LineHeight);
            graphics.FillRectangle(selectionBrush, highlight);

            var state = graphics.Save();
            graphics.IntersectClip(highlight);
            graphics.DrawString(text, Font, selectedTextBrush, left, top, format);
            graphics.Restore(state);
        }

        if (Focused && CaretBlinker.Visible && Enabled)
        {
            var caret = _view.PositionOf(_editor.Caret);
            var line = _view.Lines[_view.LineOf(_editor.Caret)];
            var x = (int)Math.Round(area.Left - _scrollX + AlignmentOffset(line.Width, area.Width) + caret.X);
            var y = (int)Math.Round(area.Top + caret.Y - _scrollY);

            using var caretBrush = new SolidBrush(palette?.Text ?? SystemColors.WindowText);
            graphics.FillRectangle(caretBrush, x, y, 1, _view.LineHeight);
        }
    }

    /// <summary>Shows the caret again after a key or click, as the blink restarts on Windows.</summary>
    private void ResetCaretBlink()
    {
        if (Focused) CaretBlinker.Restart();
    }

    protected virtual void OnBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_borderStyleEvent])?.Invoke(this, e);

    protected virtual void OnMultilineChanged(EventArgs e) => ((EventHandler?)Events[s_multilineEvent])?.Invoke(this, e);

    protected virtual void OnReadOnlyChanged(EventArgs e) => ((EventHandler?)Events[s_readOnlyEvent])?.Invoke(this, e);

    protected virtual void OnAcceptsTabChanged(EventArgs e) => ((EventHandler?)Events[s_acceptsTabEvent])?.Invoke(this, e);

    protected virtual void OnHideSelectionChanged(EventArgs e) => ((EventHandler?)Events[s_hideSelectionEvent])?.Invoke(this, e);

    protected virtual void OnModifiedChanged(EventArgs e) => ((EventHandler?)Events[s_modifiedEvent])?.Invoke(this, e);

    protected override void OnTextChanged(EventArgs e) => base.OnTextChanged(e);

    public event EventHandler? BorderStyleChanged
    {
        add => Events.AddHandler(s_borderStyleEvent, value);
        remove => Events.RemoveHandler(s_borderStyleEvent, value);
    }

    public event EventHandler? MultilineChanged
    {
        add => Events.AddHandler(s_multilineEvent, value);
        remove => Events.RemoveHandler(s_multilineEvent, value);
    }

    public event EventHandler? ReadOnlyChanged
    {
        add => Events.AddHandler(s_readOnlyEvent, value);
        remove => Events.RemoveHandler(s_readOnlyEvent, value);
    }

    public event EventHandler? AcceptsTabChanged
    {
        add => Events.AddHandler(s_acceptsTabEvent, value);
        remove => Events.RemoveHandler(s_acceptsTabEvent, value);
    }

    public event EventHandler? HideSelectionChanged
    {
        add => Events.AddHandler(s_hideSelectionEvent, value);
        remove => Events.RemoveHandler(s_hideSelectionEvent, value);
    }

    public event EventHandler? ModifiedChanged
    {
        add => Events.AddHandler(s_modifiedEvent, value);
        remove => Events.RemoveHandler(s_modifiedEvent, value);
    }

    private static readonly object s_borderStyleEvent = new();
    private static readonly object s_multilineEvent = new();
    private static readonly object s_readOnlyEvent = new();
    private static readonly object s_acceptsTabEvent = new();
    private static readonly object s_hideSelectionEvent = new();
    private static readonly object s_modifiedEvent = new();

    public override string ToString()
    {
        var text = _editor.Text;
        if (text.Length > 40) text = text[..40] + "...";

        return $"{base.ToString()}, Text: {text}";
    }

    /// <summary>One caret blinks at a time, in the focused text box, at the system's rate.</summary>
    private static class CaretBlinker
    {
        private const int BlinkMilliseconds = 530;

        private static Timer? s_timer;
        private static TextBoxBase? s_owner;

        public static bool Visible { get; private set; } = true;

        public static void Start(TextBoxBase owner)
        {
            s_owner = owner;
            Visible = true;

            s_timer ??= CreateTimer();
            s_timer.Stop();
            s_timer.Start();
        }

        public static void Stop(TextBoxBase owner)
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

                if (s_owner is { IsDisposed: false } owner) owner.Invalidate();
                else s_timer?.Stop();
            };

            return timer;
        }
    }
}
