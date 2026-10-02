using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members TextBox re-declares only to keep the public shape Microsoft
/// declares on this type: most forward to TextBoxBase, which already
/// implements the behaviour (the caret blink on handle destruction, Ctrl+A).
/// AutoCompleteMode/Source/CustomSource are stored here; <see cref="RunAutoComplete"/>
/// (subscribed to TextChanged in the constructor, TextBox.cs) gives them
/// real behaviour.
/// </summary>
/// <remarks>WndProc is skipped: it overrides a Control member p0-control.txt lists as not yet implemented.</remarks>
public partial class TextBox
{
    private AutoCompleteMode _autoCompleteMode = AutoCompleteMode.None;
    private AutoCompleteSource _autoCompleteSource = AutoCompleteSource.None;
    private AutoCompleteStringCollection? _autoCompleteCustomSource;

    [DefaultValue(AutoCompleteMode.None)]
    public AutoCompleteMode AutoCompleteMode
    {
        get => _autoCompleteMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoCompleteMode));

            _autoCompleteMode = value;
        }
    }

    [DefaultValue(AutoCompleteSource.None)]
    public AutoCompleteSource AutoCompleteSource
    {
        get => _autoCompleteSource;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoCompleteSource));

            _autoCompleteSource = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [Browsable(false)]
    public AutoCompleteStringCollection AutoCompleteCustomSource
    {
        get => _autoCompleteCustomSource ??= new AutoCompleteStringCollection();
        set => _autoCompleteCustomSource = value;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e)
    {
        HideSuggestions();
        base.OnHandleDestroyed(e);
    }

    /// <summary>Tabbing (or clicking) to another control leaves no suggestion list behind, as Windows' own does not either.</summary>
    internal override void DefaultLostFocus()
    {
        base.DefaultLostFocus();
        HideSuggestions();
    }

    protected override void OnKeyUp(KeyEventArgs e) => base.OnKeyUp(e);

    protected override void OnMouseDown(MouseEventArgs e) => base.OnMouseDown(e);

    /// <summary>
    /// While the suggestion list is open, its keys are taken here, before
    /// dialog processing: otherwise Enter and Escape would press the form's
    /// AcceptButton and CancelButton, and Up and Down could move the focus.
    /// Up/Down move the highlight, Enter accepts the highlighted row, Escape
    /// closes the list; with the list closed every key goes on as usual.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_suggestionPopup is { IsOpen: true } popup)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    HideSuggestions();
                    return true;

                case Keys.Up:
                    popup.MoveHighlight(-1);
                    return true;

                case Keys.Down:
                    popup.MoveHighlight(1);
                    return true;

                case Keys.Enter when popup.AcceptHighlighted():
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private AutoCompleteSuggestionPopup? _suggestionPopup;
    private bool _completingInline;

    /// <summary>
    /// After every text change (subscribed to TextChanged in the constructor):
    /// Modified is already true when the user typed it (set in TextEdited
    /// before TextChanged fires) and false when code assigned Text,
    /// including the inline completion this itself writes, which must not
    /// re-trigger and loop.
    /// </summary>
    private void RunAutoComplete()
    {
        if (_completingInline || !Modified || _autoCompleteMode == AutoCompleteMode.None) return;
        if (SelectionStart != TextLength) return;

        var candidates = AutoCompleteMatching.CandidatesFor(_autoCompleteSource, _autoCompleteCustomSource, null);
        var matches = AutoCompleteMatching.Match(candidates, Text);

        if (_autoCompleteMode is AutoCompleteMode.Append or AutoCompleteMode.SuggestAppend) AppendMatch(matches);

        if (_autoCompleteMode is AutoCompleteMode.Suggest or AutoCompleteMode.SuggestAppend) ShowSuggestions(matches);
        else HideSuggestions();
    }

    /// <summary>Completes the typed text with the rest of the first match, selected so the next keystroke replaces it, as Windows does.</summary>
    private void AppendMatch(List<string> matches)
    {
        if (matches.Count == 0) return;

        var typed = Text;
        var match = matches[0];
        if (match.Length <= typed.Length) return;

        _completingInline = true;
        try
        {
            Text = typed + match[typed.Length..];
        }
        finally
        {
            _completingInline = false;
        }

        Select(typed.Length, Text.Length - typed.Length);
    }

    private void ShowSuggestions(List<string> matches)
    {
        if (matches.Count == 0 || !IsHandleCreated)
        {
            HideSuggestions();
            return;
        }

        _suggestionPopup ??= new AutoCompleteSuggestionPopup(this, AcceptSuggestion);
        _suggestionPopup.ShowMatches(matches, PopupScreenBounds());
    }

    private void HideSuggestions() => _suggestionPopup?.HideSuggestions();

    private Rectangle PopupScreenBounds()
    {
        var topLeft = PointToScreen(new Point(0, Height));
        return new Rectangle(topLeft.X, topLeft.Y, Math.Max(Width, 1), 1);
    }

    private void AcceptSuggestion(string match)
    {
        _completingInline = true;
        try
        {
            Text = match;
        }
        finally
        {
            _completingInline = false;
        }

        Select(Text.Length, 0);
    }
}
