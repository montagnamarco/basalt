using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members ComboBox re-declares only to keep the public shape Microsoft
/// declares on this type: most forward straight to Control or ListControl,
/// since this implementation needs no extra behaviour beyond what the base
/// class already does. AutoCompleteMode/Source/CustomSource are stored here;
/// <see cref="OnTextChanged"/> below gives them real behaviour.
/// </summary>
/// <remarks>WndProc is skipped: it overrides a Control member p0-control.txt lists as not yet implemented.</remarks>
public partial class ComboBox
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

    /// <summary>What Items.AddRange calls for every item at once, so a subclass can batch the work instead of adding one by one.</summary>
    protected virtual void AddItemsCore(object[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        BeginUpdate();
        try
        {
            foreach (var item in value) Items.Add(item);
        }
        finally
        {
            EndUpdate();
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateHandle() => base.CreateHandle();

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnDataSourceChanged(EventArgs e) => base.OnDataSourceChanged(e);

    protected override void OnDisplayMemberChanged(EventArgs e) => base.OnDisplayMemberChanged(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnForeColorChanged(EventArgs e) => base.OnForeColorChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e)
    {
        HideSuggestions();
        base.OnHandleDestroyed(e);
    }

    protected override void OnKeyUp(KeyEventArgs e) => base.OnKeyUp(e);

    /// <summary>Tabbing (or clicking) to another control leaves no suggestion list behind, as Windows' own does not either.</summary>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        HideSuggestions();
    }

    protected override void OnMouseEnter(EventArgs e) => base.OnMouseEnter(e);

    protected override void OnMouseLeave(EventArgs e) => base.OnMouseLeave(e);

    protected override void OnParentBackColorChanged(EventArgs e) => base.OnParentBackColorChanged(e);

    protected override void OnResize(EventArgs e) => base.OnResize(e);

    protected override void OnSelectedValueChanged(EventArgs e) => base.OnSelectedValueChanged(e);

    private AutoCompleteSuggestionPopup? _suggestionPopup;
    private bool _completingInline;

    /// <summary>
    /// After every text change: _textChangedByUser (set in TextEdited, ComboBox.Edit.cs) tells a
    /// keystroke apart from code assigning Text or picking an item, including this method's own
    /// inline completion, none of which must re-trigger a suggestion or loop back into itself.
    /// </summary>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        if (_completingInline || !_textChangedByUser || !IsEditable || _autoCompleteMode == AutoCompleteMode.None) return;
        if (SelectionStart != Text.Length) return;

        var candidates = AutoCompleteMatching.CandidatesFor(_autoCompleteSource, _autoCompleteCustomSource, () => _items.Select(GetItemText));
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

    protected override void OnValidating(CancelEventArgs e) => base.OnValidating(e);

    protected override bool ProcessKeyEventArgs(ref Message m) => base.ProcessKeyEventArgs(ref m);

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) => base.ScaleControl(factor, specified);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new object? DataSource
    {
        get => base.DataSource;
        set => base.DataSource = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlatStyle));

            _flatStyle = value;
        }
    }

    private FlatStyle _flatStyle = FlatStyle.Standard;

    public override bool Focused => base.Focused;

    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    public override Size MaximumSize
    {
        get => base.MaximumSize;
        set => base.MaximumSize = value;
    }

    public override Size MinimumSize
    {
        get => base.MinimumSize;
        set => base.MinimumSize = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageChanged
    {
        add => base.BackgroundImageChanged += value;
        remove => base.BackgroundImageChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageLayoutChanged
    {
        add => base.BackgroundImageLayoutChanged += value;
        remove => base.BackgroundImageLayoutChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event PaintEventHandler? Paint
    {
        add => base.Paint += value;
        remove => base.Paint -= value;
    }

    /// <summary>
    /// A piece of a combo box identified by a native child handle: in real
    /// WinForms, one of its sub-windows (the edit, the list, the button).
    /// This implementation draws every part itself rather than hosting real
    /// child HWNDs, so the handle is kept only for API parity and the name
    /// answers with the owner's own text.
    /// </summary>
    public class ChildAccessibleObject : AccessibleObject
    {
        private readonly ComboBox _owner;
        private readonly IntPtr _handle;

        public ChildAccessibleObject(ComboBox owner, IntPtr handle)
        {
            ArgumentNullException.ThrowIfNull(owner);

            _owner = owner;
            _handle = handle;
        }

        // Overriding only the getter: the inherited setter is still reachable through any base-typed
        // reference (AccessibleObject.Name/screen readers), unlike a "new" member, which would hide
        // this part's own name from exactly the callers that read it polymorphically.
        public override string? Name => _owner.Text;
    }
}
