namespace System.Windows.Forms.Internal.Text;

/// <summary>
/// The text of an edit control with its selection and its undo, the rules
/// of the Win32 EDIT control that TextBox wraps: a single level of undo that
/// a second Undo reverses, a maximum length that typing and pasting respect
/// but setting Text does not, and a selection kept as an anchor and a caret.
/// </summary>
internal sealed class TextEditor
{
    private string _text = string.Empty;

    /// <summary>Where the selection started; the caret is the other end.</summary>
    private int _anchor;

    private int _caret;

    private string? _undoText;
    private int _undoCaret;

    public string Text => _text;

    public int Length => _text.Length;

    public int Caret => _caret;

    public int Anchor => _anchor;

    public int SelectionStart => Math.Min(_anchor, _caret);

    public int SelectionLength => Math.Abs(_caret - _anchor);

    public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

    public bool CanUndo => _undoText is not null;

    /// <summary>Zero for no limit; the EDIT control's own limit is far beyond what is typed.</summary>
    public int MaxLength { get; set; } = 32767;

    /// <summary>Replaces the whole text, as setting Text does: no undo, caret at the start.</summary>
    public void SetText(string text)
    {
        _text = text;
        _anchor = 0;
        _caret = 0;
        _undoText = null;
    }

    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, _text.Length);
        var end = Math.Clamp(start + length, 0, _text.Length);

        _anchor = start;
        _caret = end;
    }

    /// <summary>Moves the caret; with <paramref name="extend"/> the anchor stays, selecting what lies between.</summary>
    public void MoveCaret(int position, bool extend)
    {
        _caret = Math.Clamp(position, 0, _text.Length);
        if (!extend) _anchor = _caret;
    }

    /// <summary>
    /// Replaces the selection with text typed or pasted, cut to what the
    /// maximum length leaves room for; false when nothing changed.
    /// </summary>
    public bool ReplaceSelection(string replacement, bool recordUndo = true)
    {
        var start = SelectionStart;
        var length = SelectionLength;

        if (MaxLength > 0)
        {
            var room = MaxLength - (_text.Length - length);
            if (room <= 0 && replacement.Length > 0) return false;

            if (replacement.Length > room) replacement = replacement[..Math.Max(room, 0)];
        }

        if (length == 0 && replacement.Length == 0) return false;

        if (recordUndo)
        {
            _undoText = _text;
            _undoCaret = _caret;
        }

        _text = string.Concat(_text.AsSpan(0, start), replacement, _text.AsSpan(start + length));
        _caret = start + replacement.Length;
        _anchor = _caret;

        return true;
    }

    /// <summary>Deletes the selection, or the character after (or before) the caret when nothing is selected.</summary>
    public bool Delete(bool backwards, bool wholeWord)
    {
        if (SelectionLength == 0)
        {
            if (backwards)
            {
                if (_caret == 0) return false;

                _anchor = wholeWord ? WordBoundary(_caret, forward: false) : PreviousCharacter(_caret);
            }
            else
            {
                if (_caret == _text.Length) return false;

                _anchor = wholeWord ? WordBoundary(_caret, forward: true) : NextCharacter(_caret);
            }
        }

        return ReplaceSelection(string.Empty);
    }

    /// <summary>Undoes the last change; a second Undo redoes it, as the EDIT control does.</summary>
    public bool Undo()
    {
        if (_undoText is null) return false;

        var current = _text;
        var currentCaret = _caret;

        _text = _undoText;
        _caret = Math.Min(_undoCaret, _text.Length);
        _anchor = _caret;

        _undoText = current;
        _undoCaret = currentCaret;

        return true;
    }

    public void ClearUndo() => _undoText = null;

    /// <summary>The next caret stop: a surrogate pair or a CR LF pair is one step.</summary>
    public int NextCharacter(int position)
    {
        if (position >= _text.Length) return _text.Length;

        if (_text[position] == '\r' && position + 1 < _text.Length && _text[position + 1] == '\n') return position + 2;
        if (char.IsHighSurrogate(_text[position]) && position + 1 < _text.Length) return position + 2;

        return position + 1;
    }

    public int PreviousCharacter(int position)
    {
        if (position <= 0) return 0;

        if (_text[position - 1] == '\n' && position >= 2 && _text[position - 2] == '\r') return position - 2;
        if (char.IsLowSurrogate(_text[position - 1]) && position >= 2) return position - 2;

        return position - 1;
    }

    /// <summary>
    /// The next word start going forward, the previous one going back: the
    /// Ctrl+arrow stops of the EDIT control, which skip the spaces after a word.
    /// </summary>
    public int WordBoundary(int position, bool forward)
    {
        if (forward)
        {
            var index = position;
            if (index < _text.Length && IsWordCharacter(_text[index]))
            {
                while (index < _text.Length && IsWordCharacter(_text[index])) index++;
            }
            else if (index < _text.Length && !char.IsWhiteSpace(_text[index]))
            {
                index++;
            }

            while (index < _text.Length && char.IsWhiteSpace(_text[index]) && _text[index] != '\r' && _text[index] != '\n') index++;

            return index == position ? NextCharacter(position) : index;
        }

        var back = position;
        while (back > 0 && char.IsWhiteSpace(_text[back - 1]) && _text[back - 1] != '\n') back--;

        if (back > 0 && IsWordCharacter(_text[back - 1]))
        {
            while (back > 0 && IsWordCharacter(_text[back - 1])) back--;
        }
        else if (back > 0 && back == position)
        {
            back = PreviousCharacter(back);
        }

        return back;
    }

    /// <summary>The word around a position, for a double click: its start and end.</summary>
    public (int Start, int End) WordAt(int position)
    {
        position = Math.Clamp(position, 0, _text.Length);
        var start = position;
        var end = position;

        if (position < _text.Length && IsWordCharacter(_text[position]) || position > 0 && IsWordCharacter(_text[position - 1]))
        {
            while (start > 0 && IsWordCharacter(_text[start - 1])) start--;
            while (end < _text.Length && IsWordCharacter(_text[end])) end++;
        }
        else if (position < _text.Length)
        {
            end = NextCharacter(position);
        }

        // Double-clicking a word selects the spaces after it too, as the EDIT control does.
        while (end < _text.Length && _text[end] == ' ') end++;

        return (start, end);
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
