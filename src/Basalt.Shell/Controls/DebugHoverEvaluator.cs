using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Basalt.Shell.Controls;

/// <summary>
/// Shows the value of whatever the pointer rests on, while stopped.
///
/// Only while stopped: with nothing paused there is no frame to evaluate
/// against, and a tooltip reading "not available" on every identifier would be
/// worse than none at all.
/// </summary>
public sealed class DebugHoverEvaluator
{
    private readonly TextEditor _editor;
    private readonly Func<string, Task<string?>> _evaluate;
    private readonly Func<bool> _isPaused;

    private readonly Popup _popup;
    private readonly TextBlock _text;

    private string? _showing;

    public DebugHoverEvaluator(
        TextEditor editor,
        Func<string, Task<string?>> evaluate,
        Func<bool> isPaused)
    {
        _editor = editor;
        _evaluate = evaluate;
        _isPaused = isPaused;

        _text = new TextBlock
        {
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = 12,
            Margin = new Thickness(8, 6, 8, 6)
        };

        _popup = new Popup
        {
            // Anchored under the word, as Visual Studio's data tips sit, and without light dismiss:
            // that lays an input layer over the whole window, the text view then reports the
            // pointer as gone, and the tip closed the moment it opened. Moving to another word or
            // out of the text view already closes it.
            PlacementTarget = editor.TextArea.TextView,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _text
            }
        };

        var view = editor.TextArea.TextView;
        view.PointerMoved += OnPointerMoved;
        view.PointerExited += (_, _) => Hide();

        // The tip describes one word at one place: scrolling moves the word out from under it, and a
        // closed tab would otherwise leave it floating over whatever replaced the editor.
        view.ScrollOffsetChanged += (_, _) => Hide();
        view.DetachedFromVisualTree += (_, _) => Hide();
    }

    /// <summary>The text editor this tip watches, so the window can tell which file it belongs to.</summary>
    internal TextEditor Editor => _editor;

    /// <summary>The expression currently shown, for tests.</summary>
    internal string? Showing => _showing;

    internal string DisplayedText => _text.Text ?? "";

    /// <summary>
    /// Handles pointer movement.
    ///
    /// An event handler cannot be awaited, so the work is started and any
    /// failure is swallowed here: an exception escaping an async void handler
    /// would bring the application down over a tooltip.
    /// </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e) =>
        _ = HandlePointerMovedAsync(e);

    private async Task HandlePointerMovedAsync(PointerEventArgs e)
    {
        try
        {
            await EvaluateUnderPointerAsync(e).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or ArgumentOutOfRangeException)
        {
            Hide();
        }
    }

    private async Task EvaluateUnderPointerAsync(PointerEventArgs e)
    {
        if (!_isPaused())
        {
            Hide();
            return;
        }

        // TextEditor.GetPositionFromPoint takes a point relative to the whole editor, margins
        // included; one relative to the text view lands a margin's width to the right of the pointer.
        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));

        if (position is null)
        {
            Hide();
            return;
        }

        var offset = _editor.Document.GetOffset(position.Value.Location);
        var span = WordSpanAt(_editor.Document, offset);

        if (span is not var (start, length))
        {
            Hide();
            return;
        }

        var word = _editor.Document.GetText(start, length);

        if (word == _showing) return;

        _popup.PlacementRect = WordBounds(start, length);

        await ShowAsync(word).ConfigureAwait(true);
    }

    internal async Task ShowAsync(string expression)
    {
        _showing = expression;

        string? value;

        try
        {
            // ConfigureAwait(true): the popup is updated below.
            value = await _evaluate(expression).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            value = null;
        }

        // The pointer may have moved on while the debugger was answering.
        if (_showing != expression) return;

        if (value is null)
        {
            Hide();
            return;
        }

        _text.Text = $"{expression} = {value}";
        _popup.IsOpen = true;
    }

    internal void Hide()
    {
        _showing = null;
        _popup.IsOpen = false;
    }

    /// <summary>
    /// The identifier under an offset, or null if there is none.
    ///
    /// Dotted names are taken whole, so resting on "Name" in "customer.Name"
    /// evaluates the property rather than a variable that does not exist.
    /// </summary>
    internal static string? WordAt(TextDocument document, int offset) =>
        WordSpanAt(document, offset) is var (start, length) ? document.GetText(start, length) : null;

    /// <summary>Where <see cref="WordAt"/>'s word starts and how long it is, so the tip can sit under it.</summary>
    private static (int Start, int Length)? WordSpanAt(TextDocument document, int offset)
    {
        if (offset < 0 || offset > document.TextLength) return null;

        var start = offset;
        var end = offset;

        while (start > 0 && IsWordCharacter(document.GetCharAt(start - 1))) start--;
        while (end < document.TextLength && IsWordCharacter(document.GetCharAt(end))) end++;

        if (start == end) return null;

        // A leading or trailing dot would come from resting just after a member access.
        while (start < end && document.GetCharAt(start) == '.') start++;
        while (end > start && document.GetCharAt(end - 1) == '.') end--;

        return start == end || char.IsDigit(document.GetCharAt(start)) ? null : (start, end - start);
    }

    /// <summary>The word's rectangle in text-view coordinates, the anchor the tip opens under.</summary>
    private Rect WordBounds(int start, int length)
    {
        var view = _editor.TextArea.TextView;
        var first = _editor.Document.GetLocation(start);
        var last = _editor.Document.GetLocation(start + length);
        var topLeft = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(first), AvaloniaEdit.Rendering.VisualYPosition.LineTop) - view.ScrollOffset;
        var bottomRight = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(last), AvaloniaEdit.Rendering.VisualYPosition.LineBottom) - view.ScrollOffset;

        return new Rect(new Point(topLeft.X, topLeft.Y), new Point(bottomRight.X, bottomRight.Y));
    }

    private static bool IsWordCharacter(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
