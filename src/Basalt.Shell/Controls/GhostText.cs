using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;

namespace Basalt.Shell.Controls;

/// <summary>
/// Draws a suggestion in the editor without putting it in the document.
/// </summary>
/// <remarks>
/// Through a VisualLineElementGenerator, which is how AvaloniaEdit draws
/// something that is not text: the suggestion never enters the document, so
/// undo does not know about it, the file on disk does not have it, and
/// dismissing it costs nothing.
///
/// Written into the document instead — as a placeholder to be removed later —
/// every keystroke would push an edit onto the undo stack, and Ctrl+Z would
/// walk back through suggestions nobody accepted.
/// </remarks>
public sealed class GhostTextGenerator : VisualLineElementGenerator
{
    private string? _text;
    private int _position;

    /// <summary>What is being suggested, and where.</summary>
    public void Show(string text, int position)
    {
        _text = text;
        _position = position;
    }

    /// <summary>Takes the suggestion away.</summary>
    public void Clear()
    {
        _text = null;
        _position = 0;
    }

    /// <summary>Whether something is being suggested.</summary>
    public bool IsShowing => _text is { Length: > 0 };

    /// <summary>The text being suggested, or null.</summary>
    public string? Text => _text;

    /// <summary>Where the suggestion starts.</summary>
    public int Position => _position;

    public override int GetFirstInterestedOffset(int startOffset) =>
        _text is { Length: > 0 } && _position >= startOffset ? _position : -1;

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (_text is not { Length: > 0 } text || offset != _position) return null;

        return new GhostTextElement(text);
    }
}

/// <summary>One run of suggested text, drawn dimmed.</summary>
/// <remarks>
/// The visual length is the text's own and the document length is zero: it
/// takes up the room it draws in but occupies no offsets, so the caret moves
/// over it as though it were not there and every position in the document
/// still means what it meant.
///
/// Declared as one column instead, the formatter refused the run outright —
/// "The returned TextRun is too long" — and the editor threw on every draw.
/// </remarks>
internal sealed class GhostTextElement(string text)
    : VisualLineElement(text.Length, 0)
{
    public override TextRun CreateTextRun(
        int startVisualColumn, ITextRunConstructionContext context)
    {
        var properties = TextRunProperties;

        // Dimmed rather than coloured: a suggestion is not part of the code
        // and should not compete with it for attention. Half opacity reads as
        // provisional at any theme, where a fixed grey is invisible on one and
        // shouting on the other.
        properties.SetForegroundBrush(
            new SolidColorBrush(
                (properties.ForegroundBrush as ISolidColorBrush)?.Color ?? Colors.Gray,
                0.45));

        properties.SetTypeface(
            new Typeface(
                properties.Typeface.FontFamily,
                FontStyle.Italic,
                properties.Typeface.Weight));

        return new TextCharacters(text, properties);
    }
}
