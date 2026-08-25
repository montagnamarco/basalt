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
            PlacementTarget = editor,
            Placement = PlacementMode.Pointer,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _text
            }
        };

        editor.TextArea.TextView.PointerMoved += OnPointerMoved;
        editor.TextArea.TextView.PointerExited += (_, _) => Hide();
    }

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

        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor.TextArea.TextView));

        if (position is null)
        {
            Hide();
            return;
        }

        var offset = _editor.Document.GetOffset(position.Value.Location);
        var word = WordAt(_editor.Document, offset);

        if (word is null)
        {
            Hide();
            return;
        }

        if (word == _showing) return;

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
    internal static string? WordAt(TextDocument document, int offset)
    {
        if (offset < 0 || offset > document.TextLength) return null;

        var start = offset;
        var end = offset;

        while (start > 0 && IsWordCharacter(document.GetCharAt(start - 1))) start--;
        while (end < document.TextLength && IsWordCharacter(document.GetCharAt(end))) end++;

        if (start == end) return null;

        // A leading dot would come from resting just after a member access.
        var word = document.GetText(start, end - start).Trim('.');

        return word.Length == 0 || char.IsDigit(word[0]) ? null : word;
    }

    private static bool IsWordCharacter(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
