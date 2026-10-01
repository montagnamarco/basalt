using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A box the user types text into, on one line or several.</summary>
public class TextBox : TextBoxBase
{
    private bool _acceptsReturn;
    private CharacterCasing _characterCasing = CharacterCasing.Normal;
    private char _passwordChar;
    private bool _useSystemPasswordChar;
    private string _placeholderText = string.Empty;
    private ScrollBars _scrollBars = ScrollBars.None;
    private HorizontalAlignment _textAlign = HorizontalAlignment.Left;

    public TextBox()
    {
    }

    /// <summary>Whether Enter makes a new line in a multiline box instead of pressing the form's accept button.</summary>
    [DefaultValue(false)]
    public bool AcceptsReturn
    {
        get => _acceptsReturn;
        set => _acceptsReturn = value;
    }

    [DefaultValue(CharacterCasing.Normal)]
    public CharacterCasing CharacterCasing
    {
        get => _characterCasing;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CharacterCasing));

            _characterCasing = value;
        }
    }

    [DefaultValue('\0')]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public char PasswordChar
    {
        get => _passwordChar;
        set
        {
            _passwordChar = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public bool UseSystemPasswordChar
    {
        get => _useSystemPasswordChar;
        set
        {
            _useSystemPasswordChar = value;
            Invalidate();
        }
    }

    [Localizable(true)]
    [AllowNull]
    public virtual string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>The scroll bars of a multiline box; they are part of the window's frame.</summary>
    [DefaultValue(ScrollBars.None)]
    [Localizable(true)]
    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ScrollBars));
            if (value == _scrollBars) return;

            _scrollBars = value;
            UpdateStyles();
        }
    }

    [DefaultValue(HorizontalAlignment.Left)]
    [Localizable(true)]
    public HorizontalAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(HorizontalAlignment));
            if (value == _textAlign) return;

            _textAlign = value;
            Invalidate();
            OnTextAlignChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.All)]
    public override bool Multiline
    {
        get => base.Multiline;
        set
        {
            if (value == base.Multiline) return;

            base.Multiline = value;
            UpdateStyles();
        }
    }

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (Multiline)
            {
                if ((_scrollBars & ScrollBars.Vertical) != 0) parameters.Style |= WindowStyles.VerticalScroll;

                // A word-wrapping box never scrolls sideways, so it has no horizontal bar.
                if ((_scrollBars & ScrollBars.Horizontal) != 0 && !WordWrap) parameters.Style |= WindowStyles.HorizontalScroll;
            }

            return parameters;
        }
    }

    /// <summary>Pastes the given text over the selection, as if typed.</summary>
    public void Paste(string? text)
    {
        if (ReadOnly || text is null) return;

        PasteText(text);
    }

    /// <summary>Enter is the box's own key only when it accepts returns.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        if (Multiline && (keyData & Keys.Alt) == 0 && (keyData & Keys.KeyCode) == Keys.Enter) return _acceptsReturn;

        return base.IsInputKey(keyData);
    }

    private protected override bool AcceptsReturnInternal => _acceptsReturn || (ModifierKeys & Keys.Control) != 0;

    private protected override string PrepareInput(string text)
    {
        text = base.PrepareInput(text);

        return _characterCasing switch
        {
            CharacterCasing.Upper => text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            CharacterCasing.Lower => text.ToLower(System.Globalization.CultureInfo.CurrentCulture),
            _ => text,
        };
    }

    /// <summary>The system's password character is the black circle Windows shows.</summary>
    private protected override char DisplayPasswordChar => _useSystemPasswordChar ? '●' : _passwordChar;

    private protected override string PlaceholderTextInternal => _placeholderText;

    private protected override float AlignmentOffset(float lineWidth, int areaWidth) => _textAlign switch
    {
        HorizontalAlignment.Right => Math.Max(areaWidth - lineWidth - 1, 0),
        HorizontalAlignment.Center => Math.Max((areaWidth - lineWidth) / 2, 0),
        _ => 0,
    };

    protected virtual void OnTextAlignChanged(EventArgs e) => ((EventHandler?)Events[s_textAlignEvent])?.Invoke(this, e);

    public event EventHandler? TextAlignChanged
    {
        add => Events.AddHandler(s_textAlignEvent, value);
        remove => Events.RemoveHandler(s_textAlignEvent, value);
    }

    private static readonly object s_textAlignEvent = new();
}
