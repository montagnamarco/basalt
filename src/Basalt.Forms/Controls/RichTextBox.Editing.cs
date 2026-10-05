using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;
using System.Windows.Forms.Internal.RichText;

namespace System.Windows.Forms;

/// <summary>
/// Everything that keeps <see cref="RichDocument"/> in step with the plain
/// text TextBoxBase's inherited editor holds, and that replaces the plain
/// editor's painting, hit testing and key/mouse handling with ones that
/// know about runs: the two engines never run at the same time on the same
/// edit, one always defers to the other.
/// </summary>
public partial class RichTextBox
{
    private readonly record struct FontKey(string Family, float Size, bool Bold, bool Italic, bool Underline, bool Strikeout)
    {
        public static FontKey Of(CharacterFormat format) => new(format.FontFamily, format.FontSize, format.Bold, format.Italic, format.Underline, format.Strikeout);

        public Font ToFont() => new(Family, Size, Style);

        private FontStyle Style =>
            (Bold ? FontStyle.Bold : FontStyle.Regular) |
            (Italic ? FontStyle.Italic : FontStyle.Regular) |
            (Underline ? FontStyle.Underline : FontStyle.Regular) |
            (Strikeout ? FontStyle.Strikeout : FontStyle.Regular);
    }

    private readonly record struct UndoSnapshot(RichDocument Document, int SelectionStart, int SelectionLength);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if ((_scrollBars & RichTextBoxScrollBars.Horizontal) != 0) parameters.Style |= WindowStyles.HorizontalScroll;
            if ((_scrollBars & RichTextBoxScrollBars.Vertical) != 0) parameters.Style |= WindowStyles.VerticalScroll;

            return parameters;
        }
    }

    internal override void DefaultGotFocus()
    {
        RichCaretBlink.Start(this);
        Invalidate();
    }

    internal override void DefaultLostFocus()
    {
        RichCaretBlink.Stop(this);
        Invalidate();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        RichCaretBlink.Stop(this);
        base.OnHandleDestroyed(e);
    }

    /// <summary>TextBoxBase's own blink timer is private to it; a formatted caret needs its own, started independently.</summary>
    private static class RichCaretBlink
    {
        private const int BlinkMilliseconds = 530;

        private static Timer? s_timer;
        private static RichTextBox? s_owner;

        public static bool Visible { get; private set; } = true;

        public static void Start(RichTextBox owner)
        {
            s_owner = owner;
            Visible = true;

            s_timer ??= CreateTimer();
            s_timer.Stop();
            s_timer.Start();
        }

        public static void Stop(RichTextBox owner)
        {
            if (s_owner != owner) return;

            s_owner = null;
            s_timer?.Stop();
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

    // --- Keeping the document in step with the plain text ------------------------------------------------------

    /// <summary>
    /// Every edit TextBoxBase's inherited editor makes fires TextChanged; this is where the document catches up.
    /// Subscribing (rather than overriding OnTextChanged, which Microsoft's RichTextBox does not do either) keeps
    /// RichTextBox's own public surface exactly Microsoft's, and the constructor subscribes before any user code
    /// can, so the document is already in step by the time an external TextChanged handler runs.
    /// </summary>
    private void OnBaseTextChanged(object? sender, EventArgs e)
    {
        if (!_suppressSync) SyncDocumentFromPlainText();
    }

    private void SyncDocumentFromPlainText()
    {
        var newText = base.Text;
        var oldText = _syncedPlainText;

        if (newText != oldText)
        {
            var prefix = CommonPrefixLength(oldText, newText);
            var maxSuffix = Math.Min(oldText.Length, newText.Length) - prefix;
            var suffix = Math.Min(CommonSuffixLength(oldText, newText), maxSuffix);

            var removedLength = oldText.Length - prefix - suffix;
            var insertedText = newText.Substring(prefix, newText.Length - prefix - suffix);

            var format = _insertionFormat ?? _document.FormatBeforeCaret(prefix);
            _insertionFormat = null;

            if (removedLength > 0) _document.Remove(prefix, removedLength);
            if (insertedText.Length > 0) _document.Insert(prefix, insertedText, format);

            _syncedPlainText = newText;
        }

        InvalidateLayout();
        RebuildLinks();
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[i] == b[i]) i++;

        return i;
    }

    private static int CommonSuffixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[^(i + 1)] == b[^(i + 1)]) i++;

        return i;
    }

    /// <summary>Pushes the document's plain text into the inherited editor without re-running the diff sync: used to replay a snapshot or a freshly parsed RTF.</summary>
    private void ReplaceBaseTextFromDocument()
    {
        _suppressSync = true;
        base.Text = _document.PlainText;
        _syncedPlainText = base.Text;
        _suppressSync = false;
        InvalidateLayout();
        RebuildLinks();
    }

    // --- Undo and redo: a stack of whole-document snapshots, simple but correct for formatting-only edits too ---

    private void PushUndo()
    {
        _undoStack.Add(new UndoSnapshot(_document.Clone(), SelectionStart, SelectionLength));
        if (_undoStack.Count > 200) _undoStack.RemoveAt(0);

        _redoStack.Clear();
    }

    internal override bool CanUndoCore => _undoStack.Count > 0;

    internal override void UndoCore()
    {
        if (_undoStack.Count == 0) return;

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(new UndoSnapshot(_document.Clone(), SelectionStart, SelectionLength));

        RestoreSnapshot(snapshot);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(new UndoSnapshot(_document.Clone(), SelectionStart, SelectionLength));

        RestoreSnapshot(snapshot);
    }

    private void RestoreSnapshot(UndoSnapshot snapshot)
    {
        _document.ReplaceWith(snapshot.Document);
        ReplaceBaseTextFromDocument();

        var start = Math.Clamp(snapshot.SelectionStart, 0, TextLength);
        var length = Math.Clamp(snapshot.SelectionLength, 0, TextLength - start);
        Select(start, length);

        base.OnTextChanged(EventArgs.Empty);
    }

    // --- Replacing the selection: the one place that checks SelectionProtected before mutating -------------------

    // Replacing the selection moves the caret after the new text, a selection change like any other.
    private void ReplaceSelectionDirect(string text)
    {
        base.SelectedText = text;
        AfterSelectionChanged();
    }

    [AllowNull]
    public override string SelectedText
    {
        get => base.SelectedText;
        set
        {
            value ??= string.Empty;
            if (_document.AnyProtected(SelectionStart, SelectionLength)) { OnProtected(EventArgs.Empty); return; }

            PushUndo();
            ReplaceSelectionDirect(value);
        }
    }

    internal override void CopyCore()
    {
        if (SelectionLength > 0) PlatformClipboard.SetText(SelectedText);
    }

    internal override void CutCore()
    {
        if (SelectionLength == 0 || ReadOnly) return;
        if (_document.AnyProtected(SelectionStart, SelectionLength)) { OnProtected(EventArgs.Empty); return; }

        PlatformClipboard.SetText(SelectedText);
        if (IsDisposed) return;

        PushUndo();
        ReplaceSelectionDirect(string.Empty);
    }

    internal override void PasteCore()
    {
        if (ReadOnly) return;

        var text = PlatformClipboard.GetText();
        if (string.IsNullOrEmpty(text) || IsDisposed) return;
        if (_document.AnyProtected(SelectionStart, SelectionLength)) { OnProtected(EventArgs.Empty); return; }

        PushUndo();
        ReplaceSelectionDirect(text);
    }

    internal override void AppendTextCore(string text)
    {
        PushUndo();
        Select(TextLength, 0);
        ReplaceSelectionDirect(text);
        ScrollToCaretRich();
    }

    private void ApplyCharacterFormat(Func<CharacterFormat, CharacterFormat> mutate)
    {
        if (SelectionLength > 0)
        {
            if (_document.AnyProtected(SelectionStart, SelectionLength)) { OnProtected(EventArgs.Empty); return; }

            PushUndo();
            _document.SetCharacterFormat(SelectionStart, SelectionLength, mutate);
            InvalidateLayout();
            Invalidate();
        }
        else
        {
            _insertionFormat = mutate(_insertionFormat ?? _document.FormatBeforeCaret(SelectionStart));
        }
    }

    private void ApplyParagraphFormat(Action<ParagraphFormat> mutate)
    {
        PushUndo();
        _document.SetParagraphFormat(SelectionStart, SelectionLength, mutate);
        InvalidateLayout();
        Invalidate();
    }

    // --- Keyboard and mouse: hit testing goes through the rich layout, not TextBoxBase's plain one ---------------

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (e.Control && !e.Shift && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; return; }
        if (e.Control && e.Shift && e.KeyCode == Keys.Z) { Redo(); e.Handled = true; return; }

        if (!ReadOnly && e.KeyCode == Keys.Delete && !e.Shift)
        {
            var length = SelectionLength == 0 ? (SelectionStart < TextLength ? 1 : 0) : SelectionLength;
            if (_document.AnyProtected(SelectionStart, length)) { OnProtected(EventArgs.Empty); e.Handled = true; return; }

            PushUndo();
        }

        base.DefaultKeyDown(e);
        AfterSelectionChanged();
    }

    internal override void DefaultKeyPress(KeyPressEventArgs e)
    {
        if (!ReadOnly)
        {
            var (start, length) = e.KeyChar == '\b' && SelectionLength == 0
                ? (Math.Max(SelectionStart - 1, 0), SelectionStart > 0 ? 1 : 0)
                : (SelectionStart, Math.Max(SelectionLength, 1));

            if (_document.AnyProtected(start, length)) { OnProtected(EventArgs.Empty); return; }

            PushUndo();
        }

        base.DefaultKeyPress(e);
        AfterSelectionChanged();
    }

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (CanSelect) Focus();

        EnsureLayout();
        var index = IndexAtPoint(e.Location);

        var link = _detectUrls ? LinkAt(index) : null;
        if (link is { } found)
        {
            OnLinkClicked(new LinkClickedEventArgs(Text.Substring(found.Start, found.Length), found.Start, found.Length));
        }

        if (e.Clicks >= 2 || _autoWordSelection)
        {
            var (start, end) = WordAt(index);
            Select(start, end - start);
        }
        else
        {
            Select(index, 0);
            _selectingWithMouse = true;
        }

        AfterSelectionChanged();
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_selectingWithMouse || (e.Button & MouseButtons.Left) == 0) return;

        var index = IndexAtPoint(e.Location);
        var length = index - SelectionStart;
        Select(SelectionStart, length);

        AfterSelectionChanged();
    }

    internal override void DefaultMouseUp(MouseEventArgs e) => _selectingWithMouse = false;

    private int IndexAtPoint(Point point)
    {
        var area = TextDisplayArea();

        return _layout.IndexAt(new PointF(point.X - area.X + _scrollX, point.Y - area.Y + _scrollY));
    }

    private (int Start, int End) WordAt(int index)
    {
        var text = Text;
        index = Math.Clamp(index, 0, text.Length);
        var start = index;
        var end = index;

        bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        if ((index < text.Length && IsWordChar(text[index])) || (index > 0 && IsWordChar(text[index - 1])))
        {
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
        }
        else if (index < text.Length)
        {
            end = index + 1;
        }

        return (start, end);
    }

    // The selection last reported, so SelectionChanged is raised once per real change (as EN_SELCHANGE),
    // whichever path got there: the rich editing code below or TextBoxBase's Select/SelectAll/SelectionStart.
    private (int Start, int Length) _reportedSelection;

    internal override void SelectionChangedInternal() => AfterSelectionChanged();

    private void AfterSelectionChanged()
    {
        ScrollToCaretRich();

        var selection = (SelectionStart, SelectionLength);
        if (selection == _reportedSelection) return;

        _reportedSelection = selection;
        OnSelectionChanged(EventArgs.Empty);
    }

    private void ScrollToCaretRich()
    {
        EnsureLayout();

        var area = TextDisplayArea();
        var caret = _layout.PositionOf(SelectionStart + SelectionLength);

        if (caret.X - _scrollX > area.Width - 1) _scrollX = caret.X - area.Width + 1;
        if (caret.X - _scrollX < 0) _scrollX = Math.Max(caret.X - area.Width / 3f, 0);

        if (caret.Y - _scrollY + _layout.LineHeight > area.Height) _scrollY = caret.Y + _layout.LineHeight - area.Height;
        if (caret.Y - _scrollY < 0) _scrollY = caret.Y;

        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(_layout.TotalWidth - area.Width + 1, 0));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(_layout.TotalHeight - area.Height, 0));

        Invalidate();
    }

    // --- Layout, painting and the measurements that read it ------------------------------------------------------

    private void InvalidateLayout()
    {
        _layoutDirty = true;
        Invalidate();
    }

    private Rectangle TextDisplayArea()
    {
        var client = ClientRectangle;

        return Rectangle.FromLTRB(client.Left + 1, client.Top + 1, client.Right - 1, client.Bottom - 1);
    }

    private void EnsureLayout()
    {
        var wrapWidth = Math.Max(TextDisplayArea().Width - _rightMargin, 1);

        if (!_layoutDirty && wrapWidth == _laidOutWidth) return;

        var before = _layout.TotalHeight;

        _laidOutWidth = wrapWidth;
        _layout.Layout(_document, wrapWidth, _zoomFactor);
        _layoutDirty = false;

        if (_layout.TotalHeight != before) OnContentsResized(new ContentsResizedEventArgs(new Rectangle(0, 0, (int)_layout.TotalWidth, (int)_layout.TotalHeight)));
    }

    public override int GetCharIndexFromPosition(Point pt)
    {
        EnsureLayout();
        var area = TextDisplayArea();
        var index = _layout.IndexAt(new PointF(pt.X - area.X + _scrollX, pt.Y - area.Y + _scrollY));

        return Math.Min(index, Math.Max(TextLength - 1, 0));
    }

    public override Point GetPositionFromCharIndex(int index)
    {
        if (index < 0 || index >= TextLength) return Point.Empty;

        EnsureLayout();
        var area = TextDisplayArea();
        var position = _layout.PositionOf(index);

        return new Point((int)(position.X - _scrollX) + area.X, (int)(position.Y - _scrollY) + area.Y);
    }

    public override int GetLineFromCharIndex(int index)
    {
        EnsureLayout();

        return _layout.LineOf(index);
    }

    // The line starts of the formatted layout, which TextBoxBase's own plain view would get wrong when a line mixes fonts.

    internal override int FirstCharIndexFromLineCore(int lineNumber)
    {
        EnsureLayout();

        return lineNumber < _layout.Lines.Count ? _layout.Lines[lineNumber].Start : -1;
    }

    internal override int FirstCharIndexOfCurrentLineCore(int caret)
    {
        EnsureLayout();

        return _layout.Lines.Count == 0 ? 0 : _layout.Lines[_layout.LineOf(caret)].Start;
    }

    /// <summary>Window white normally, the control colour when disabled or read-only, unless a colour was set: TextBoxBase's own rule, repeated here since its EffectiveBackColor is private to it.</summary>
    private Color EffectiveBackColor
    {
        get
        {
            if (ShouldSerializeBackColor()) return BackColor;

            return Enabled && !ReadOnly ? SystemColors.Window : SystemColors.Control;
        }
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        EnsureLayout();

        var graphics = e.Graphics;
        var area = TextDisplayArea();

        using (var background = new SolidBrush(EffectiveBackColor))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        var state = graphics.Save();
        graphics.IntersectClip(area);

        using var fonts = new RichFonts();
        var selectionStart = SelectionStart;
        var selectionEnd = SelectionStart + SelectionLength;
        var showSelection = SelectionLength > 0 && (Focused || !HideSelection);

        foreach (var line in _layout.Lines)
        {
            var top = area.Top + line.Y - _scrollY;
            if (top + line.Height < area.Top || top > area.Bottom) continue;

            var left = area.Left - _scrollX + line.Indent;

            if (line.Paragraph.Bullet && line == FirstLineOfItsParagraph(line)) DrawBullet(graphics, fonts, line, left, top);

            foreach (var piece in line.Pieces)
            {
                var font = fonts.Get(piece.Format, _zoomFactor);
                var pieceLeft = left + line.X(piece.Start) - line.Indent;
                var offsetTop = top - piece.Format.CharOffset * _zoomFactor;

                if (piece.Format.BackColor != Color.Empty)
                {
                    using var highlight = new SolidBrush(piece.Format.BackColor);
                    graphics.FillRectangle(highlight, pieceLeft, top, piece.Width, line.Height);
                }

                using var brush = new SolidBrush(Enabled ? piece.Format.Color : SystemColors.GrayText);
                graphics.DrawString(piece.Text, font, brush, pieceLeft, offsetTop, TypographicFormat);

                if (piece.Format.Underline) DrawUnderline(graphics, piece, pieceLeft, top, line.Height, brush);
                if (piece.Format.Strikeout) DrawStrikeout(graphics, piece, pieceLeft, top, line.Height, brush);
            }

            if (showSelection) PaintSelectionOnLine(graphics, line, left, top, selectionStart, selectionEnd);
        }

        if (Focused && CaretVisible && Enabled)
        {
            var caretPosition = _layout.PositionOf(selectionEnd);
            var x = (int)Math.Round(area.Left - _scrollX + caretPosition.X);
            var y = (int)Math.Round(area.Top - _scrollY + caretPosition.Y);

            using var caretBrush = new SolidBrush(SystemColors.WindowText);
            graphics.FillRectangle(caretBrush, x, y, 1, (int)_layout.LineHeight);
        }

        graphics.Restore(state);
    }

    /// <summary>Whether the blinking caret is in its visible phase right now; TextBoxBase keeps the single shared blink timer.</summary>
    private bool CaretVisible => RichCaretBlink.Visible;

    private RichLayout.Line FirstLineOfItsParagraph(RichLayout.Line line)
    {
        foreach (var candidate in _layout.Lines)
        {
            if (candidate.Paragraph == line.Paragraph) return candidate;
        }

        return line;
    }

    private void DrawBullet(Graphics graphics, RichFonts fonts, RichLayout.Line line, float left, float top)
    {
        var format = line.Pieces.Count > 0 ? line.Pieces[0].Format : _document.DefaultFormat;
        var font = fonts.Get(format, _zoomFactor);

        using var brush = new SolidBrush(Enabled ? format.Color : SystemColors.GrayText);
        graphics.DrawString("•", font, brush, left - 14, top, TypographicFormat);
    }

    private static void DrawUnderline(Graphics graphics, RichLayout.Piece piece, float left, float top, float lineHeight, Brush brush)
    {
        var y = top + lineHeight - 2;
        graphics.DrawLine(new Pen(brush is SolidBrush solid ? solid.Color : SystemColors.WindowText), left, y, left + piece.Width, y);
    }

    private static void DrawStrikeout(Graphics graphics, RichLayout.Piece piece, float left, float top, float lineHeight, Brush brush)
    {
        var y = top + lineHeight / 2f;
        using var pen = new Pen(brush is SolidBrush solid ? solid.Color : SystemColors.WindowText);
        graphics.DrawLine(pen, left, y, left + piece.Width, y);
    }

    private void PaintSelectionOnLine(Graphics graphics, RichLayout.Line line, float left, float top, int selectionStart, int selectionEnd)
    {
        var from = Math.Max(selectionStart, line.Start);
        var to = Math.Min(selectionEnd, line.End);
        if (from >= to) return;

        var x1 = left + line.X(from) - line.Indent;
        var x2 = left + line.X(to) - line.Indent;

        var highlightArea = RectangleF.FromLTRB(x1, top, x2, top + line.Height);
        var highlightBrush = Focused ? SystemColors.Highlight : SystemColors.Control;
        var textBrush = Focused ? SystemColors.HighlightText : SystemColors.WindowText;

        using var back = new SolidBrush(highlightBrush);
        graphics.FillRectangle(back, highlightArea);

        var state = graphics.Save();
        graphics.IntersectClip(highlightArea);

        using var fonts = new RichFonts();
        using var fore = new SolidBrush(textBrush);
        foreach (var piece in line.Pieces)
        {
            var font = fonts.Get(piece.Format, _zoomFactor);
            var pieceLeft = left + line.X(piece.Start) - line.Indent;
            graphics.DrawString(piece.Text, font, fore, pieceLeft, top, TypographicFormat);
        }

        graphics.Restore(state);
    }

    private static readonly StringFormat TypographicFormat = CreateTypographicFormat();

    private static StringFormat CreateTypographicFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None;

        return format;
    }

    // --- Links --------------------------------------------------------------------------------------------------

    private static readonly Regex UrlPattern = new(@"(https?://|www\.)[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void RebuildLinks()
    {
        _links = _detectUrls
            ? [.. UrlPattern.Matches(base.Text).Select(match => (match.Index, match.Length))]
            : [];
    }

    private (int Start, int Length)? LinkAt(int index)
    {
        foreach (var link in _links)
        {
            if (index >= link.Start && index < link.Start + link.Length) return link;
        }

        return null;
    }

    // --- Find -----------------------------------------------------------------------------------------------------

    public int Find(string str) => Find(str, 0, -1, RichTextBoxFinds.None);

    public int Find(string str, RichTextBoxFinds options) => Find(str, 0, -1, options);

    public int Find(string str, int start, RichTextBoxFinds options) => Find(str, start, -1, options);

    public int Find(string str, int start, int end, RichTextBoxFinds options)
    {
        ArgumentNullException.ThrowIfNull(str);

        var content = base.Text;
        start = Math.Clamp(start, 0, content.Length);
        var limit = end < 0 ? content.Length : Math.Clamp(end, start, content.Length);

        if (str.Length == 0 || start > limit - str.Length) return -1;

        var comparison = (options & RichTextBoxFinds.MatchCase) != 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var wholeWord = (options & RichTextBoxFinds.WholeWord) != 0;
        var reverse = (options & RichTextBoxFinds.Reverse) != 0;

        var found = -1;
        var index = start;
        while (index <= limit - str.Length)
        {
            var candidate = content.IndexOf(str, index, limit - index, comparison);
            if (candidate < 0 || candidate + str.Length > limit) break;

            if (!wholeWord || IsWholeWord(content, candidate, str.Length))
            {
                found = candidate;
                if (!reverse) break;
            }

            index = candidate + 1;
        }

        if (found < 0) return -1;

        Select(found, str.Length);
        AfterSelectionChanged();
        return found;
    }

    public int Find(char[] characterSet) => Find(characterSet, 0, -1);

    public int Find(char[] characterSet, int start) => Find(characterSet, start, -1);

    public int Find(char[] characterSet, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(characterSet);

        var content = base.Text;
        start = Math.Clamp(start, 0, content.Length);
        var limit = end < 0 ? content.Length : Math.Clamp(end, start, content.Length);
        if (start >= limit) return -1;

        var index = content.IndexOfAny(characterSet, start, limit - start);
        if (index < 0) return -1;

        Select(index, 1);
        AfterSelectionChanged();
        return index;
    }

    private static bool IsWholeWord(string text, int index, int length)
    {
        var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var after = index + length >= text.Length || !char.IsLetterOrDigit(text[index + length]);

        return before && after;
    }

    // --- Files: RTF or plain text, 8-bit or Unicode ---------------------------------------------------------------

    public void LoadFile(string path) => LoadFile(path, path.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) ? RichTextBoxStreamType.RichText : RichTextBoxStreamType.PlainText);

    public void LoadFile(string path, RichTextBoxStreamType fileType)
    {
        using var stream = File.OpenRead(path);
        LoadFile(stream, fileType);
    }

    public void LoadFile(Stream data, RichTextBoxStreamType fileType)
    {
        ArgumentNullException.ThrowIfNull(data);

        var encoding = fileType == RichTextBoxStreamType.UnicodePlainText ? Encoding.Unicode : Encoding.UTF8;
        using var reader = new StreamReader(data, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = reader.ReadToEnd();

        if (fileType is RichTextBoxStreamType.RichText or RichTextBoxStreamType.RichNoOleObjs)
        {
            _document = RtfReader.Read(content, _document.DefaultFormat);
        }
        else
        {
            _document.Clear();
            _document.SetPlainText(content);
        }

        ReplaceBaseTextFromDocument();
        Select(0, 0);
        _undoStack.Clear();
        _redoStack.Clear();
        base.OnTextChanged(EventArgs.Empty);
    }

    public void SaveFile(string path) => SaveFile(path, path.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) ? RichTextBoxStreamType.RichText : RichTextBoxStreamType.PlainText);

    public void SaveFile(string path, RichTextBoxStreamType fileType)
    {
        using var stream = File.Create(path);
        SaveFile(stream, fileType);
    }

    public void SaveFile(Stream data, RichTextBoxStreamType fileType)
    {
        ArgumentNullException.ThrowIfNull(data);

        var encoding = fileType == RichTextBoxStreamType.UnicodePlainText ? Encoding.Unicode : Encoding.UTF8;
        var content = fileType is RichTextBoxStreamType.RichText or RichTextBoxStreamType.RichNoOleObjs
            ? RtfWriter.Write(_document)
            : _document.PlainText;

        using var writer = new StreamWriter(data, encoding, leaveOpen: true);
        writer.Write(content);
    }
}
