using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>
/// The editor's side of inline suggestions: what it hands the controller and
/// the few things it still answers for. The behaviour is in
/// <see cref="InlineSuggestionController"/>.
/// </summary>
public sealed partial class CodeEditor
{
    private readonly GhostTextGenerator _ghostText = new();
    private InlineSuggestionController _inline = null!;

    /// <summary>
    /// Who suggests what to write next, when anybody does.
    /// </summary>
    /// <remarks>
    /// Settable so a test can supply one that answers immediately: the real
    /// providers need an account and a network, and an editor whose suggestion
    /// path is only exercised against those is one nobody can test.
    /// </remarks>
    public Basalt.Extensibility.IInlineSuggestionProvider? SuggestionProvider { get; set; }

    /// <summary>Whether something is being suggested.</summary>
    public bool IsSuggesting => _inline.IsShowing;

    /// <summary>
    /// Whether the window is the one in use. A suggestion paid for while
    /// somebody is in another application is not wanted, and a request asked
    /// for a hidden tab is for a document nobody is looking at.
    /// </summary>
    internal Func<bool>? WindowActiveOverride { get; set; }

    /// <summary>The limits in force, for the tests.</summary>
    internal InlineSuggestionOptions InlineOptions => _inline.Options;

    /// <summary>How many suggestions were asked for, for the tests.</summary>
    internal int InlineRequestsForTests => _inline.RequestsMade;

    /// <summary>What the ghost reads, for the tests.</summary>
    internal string GhostTextForTests => _ghostText.Text ?? "";

    /// <summary>Writes the suggestion, or its next word, into the document.</summary>
    public bool AcceptSuggestion(bool wordOnly = false) => _inline.Accept(wordOnly);

    /// <summary>Presses a key in the text area as a person would, for the tests.</summary>
    /// <returns>Whether the editor took the key.</returns>
    internal bool PressForTests(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers = default)
    {
        var press = new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers
        };

        _editor.TextArea.RaiseEvent(press);

        return press.Handled;
    }

    /// <summary>The editor finished typing something itself; see <see cref="InlineSuggestionController.Typed"/>.</summary>
    private void SuggestionTyped() => _inline.Typed();

    /// <summary>Takes the suggestion away and stops asking for one.</summary>
    internal void DismissSuggestion() => _inline.Cancel();

    private void InstallInlineSuggestions()
    {
        // Registered even with nothing signed in: it draws nothing until
        // something is suggested, and adding it later would mean rebuilding
        // the view.
        _editor.TextArea.TextView.ElementGenerators.Add(_ghostText);

        _inline = new InlineSuggestionController(
            _editor,
            _ghostText,
            () => SuggestionProvider,
            () => _document.FilePath,
            isBlocked: () => _completionWindow is not null
                             || _snippets.IsActive
                             || _smartTagMenu is { IsOpen: true },
            isActive: () => WindowActiveOverride?.Invoke()
                            ?? (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is Window { IsActive: true }),
            isSuppressed: () => _suppressTextChanged,
            GhostBrush,
            // Once in the status bar, not on every pause: the controller
            // decides when, this says where.
            message => _shell.StatusMessage = Localizer.Get(StringKeys.InlineFailed, message),
            // As a typing edit: it is not formatted as a separate step when
            // the caret leaves its line, and one undo removes all of it.
            applyEdit: edit =>
            {
                ApplyTypingEdit(edit);
                OnTextChanged(this, EventArgs.Empty);
            });

        DetachedFromVisualTree += (_, _) => _inline.Cancel();
    }

    /// <summary>
    /// The theme's secondary text colour, read where the editor is, so a
    /// suggestion is readable in light and dark and follows a theme change.
    /// </summary>
    private IBrush? GhostBrush() =>
        this.TryFindResource("ChromeSecondaryForeground", ActualThemeVariant, out var found)
        && found is Color colour
            ? new SolidColorBrush(colour)
            : null;
}
