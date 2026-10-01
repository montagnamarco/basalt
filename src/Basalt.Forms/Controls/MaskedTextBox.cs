using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// A box whose content must fit a mask (a phone number, a date, a serial
/// number): a <see cref="System.ComponentModel.MaskedTextProvider"/> from the
/// BCL holds the mask's state, and this control keeps the inherited edit's
/// displayed text in step with it on every keystroke.
/// </summary>
[DefaultEvent("MaskInputRejected")]
[DefaultBindingProperty("Text")]
[DefaultProperty("Mask")]
public partial class MaskedTextBox : TextBoxBase
{
    private MaskedTextProvider? _provider;

    private string _mask = string.Empty;
    private char _promptChar = '_';
    private char _passwordChar = ' ';
    private bool _useSystemPasswordChar;
    private bool _allowPromptAsInput = true;
    private bool _asciiOnly;
    private bool _beepOnError;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private IFormatProvider? _formatProvider;
    private MaskFormat _cutCopyMaskFormat = MaskFormat.IncludeLiterals;
    private MaskFormat _textMaskFormat = MaskFormat.IncludeLiterals;
    private bool _hidePromptOnLeave;
    private InsertKeyMode _insertKeyMode = InsertKeyMode.Default;
    private bool _overwriteMode = true;
    private bool _rejectInputOnFirstFailure;
    private bool _resetOnPrompt = true;
    private bool _resetOnSpace = true;
    private bool _skipLiterals = true;
    private HorizontalAlignment _textAlign = HorizontalAlignment.Left;
    private Type? _validatingType;

    public MaskedTextBox()
    {
    }

    public MaskedTextBox(string mask)
    {
        Mask = mask;
    }

    public MaskedTextBox(MaskedTextProvider maskedTextProvider)
    {
        ArgumentNullException.ThrowIfNull(maskedTextProvider);

        _provider = maskedTextProvider;
        _mask = maskedTextProvider.Mask;
        _promptChar = maskedTextProvider.PromptChar;
        _culture = maskedTextProvider.Culture;
        _asciiOnly = maskedTextProvider.AsciiOnly;
        _allowPromptAsInput = maskedTextProvider.AllowPromptAsInput;
        _resetOnPrompt = maskedTextProvider.ResetOnPrompt;
        _resetOnSpace = maskedTextProvider.ResetOnSpace;
        _skipLiterals = maskedTextProvider.SkipLiterals;

        SyncDisplayText(0);
    }

    [DefaultValue("")]
    [MergableProperty(false)]
    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => _provider is null ? base.Text : FormatText(_textMaskFormat);
        set => SetFullText(value);
    }

    [DefaultValue("")]
    public string Mask
    {
        get => _mask;
        set
        {
            value ??= string.Empty;
            if (value == _mask) return;

            _mask = value;
            RebuildProvider();
            OnMaskChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MaskedTextProvider? MaskedTextProvider => _provider;

    [Browsable(false)]
    public bool MaskFull => _provider?.MaskFull ?? false;

    [Browsable(false)]
    public bool MaskCompleted => _provider?.MaskCompleted ?? false;

    [DefaultValue('_')]
    [Localizable(true)]
    public char PromptChar
    {
        get => _promptChar;
        set
        {
            if (value == _promptChar) return;

            _promptChar = value;
            if (_provider is null) return;

            _provider.PromptChar = value;
            SyncDisplayText(SelectionStart);
        }
    }

    /// <summary>
    /// Space, not NUL, is the "no password character" value here: the default
    /// value Microsoft declares for this property is a space, so a freshly
    /// constructed box must still show its real content.
    /// </summary>
    [DefaultValue(' ')]
    public char PasswordChar
    {
        get => _passwordChar;
        set
        {
            if (value == _passwordChar) return;

            _passwordChar = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool UseSystemPasswordChar
    {
        get => _useSystemPasswordChar;
        set
        {
            if (value == _useSystemPasswordChar) return;

            _useSystemPasswordChar = value;
            Invalidate();
        }
    }

    private protected override char DisplayPasswordChar
    {
        get
        {
            if (_useSystemPasswordChar) return '●';

            return _passwordChar == ' ' ? '\0' : _passwordChar;
        }
    }

    [DefaultValue(true)]
    public bool AllowPromptAsInput
    {
        get => _allowPromptAsInput;
        set
        {
            if (value == _allowPromptAsInput) return;

            _allowPromptAsInput = value;
            RebuildProviderPreservingContent();
        }
    }

    [DefaultValue(false)]
    public bool AsciiOnly
    {
        get => _asciiOnly;
        set
        {
            if (value == _asciiOnly) return;

            _asciiOnly = value;
            RebuildProviderPreservingContent();
        }
    }

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            value ??= CultureInfo.CurrentCulture;
            if (value.Equals(_culture)) return;

            _culture = value;
            RebuildProviderPreservingContent();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IFormatProvider? FormatProvider
    {
        get => _formatProvider;
        set => _formatProvider = value;
    }

    [DefaultValue(false)]
    public bool BeepOnError
    {
        get => _beepOnError;
        // A system beep has no headless equivalent; the flag is stored and honoured as a documented no-op.
        set => _beepOnError = value;
    }

    [DefaultValue(MaskFormat.IncludeLiterals)]
    public MaskFormat CutCopyMaskFormat
    {
        get => _cutCopyMaskFormat;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(MaskFormat));

            _cutCopyMaskFormat = value;
        }
    }

    [DefaultValue(MaskFormat.IncludeLiterals)]
    public MaskFormat TextMaskFormat
    {
        get => _textMaskFormat;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(MaskFormat));

            _textMaskFormat = value;
        }
    }

    [DefaultValue(false)]
    public bool HidePromptOnLeave
    {
        get => _hidePromptOnLeave;
        set => _hidePromptOnLeave = value;
    }

    [DefaultValue(InsertKeyMode.Default)]
    public InsertKeyMode InsertKeyMode
    {
        get => _insertKeyMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(InsertKeyMode));
            if (value == _insertKeyMode) return;

            var wasOverwrite = IsOverwriteMode;
            _insertKeyMode = value;
            if (IsOverwriteMode != wasOverwrite) OnIsOverwriteModeChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    public bool IsOverwriteMode => _insertKeyMode switch
    {
        InsertKeyMode.Insert => false,
        InsertKeyMode.Overwrite => true,
        _ => _overwriteMode,
    };

    [DefaultValue(false)]
    public bool RejectInputOnFirstFailure
    {
        get => _rejectInputOnFirstFailure;
        set => _rejectInputOnFirstFailure = value;
    }

    [DefaultValue(true)]
    public bool ResetOnPrompt
    {
        get => _resetOnPrompt;
        set
        {
            if (value == _resetOnPrompt) return;

            _resetOnPrompt = value;
            if (_provider is not null) _provider.ResetOnPrompt = value;
        }
    }

    [DefaultValue(true)]
    public bool ResetOnSpace
    {
        get => _resetOnSpace;
        set
        {
            if (value == _resetOnSpace) return;

            _resetOnSpace = value;
            if (_provider is not null) _provider.ResetOnSpace = value;
        }
    }

    [DefaultValue(true)]
    public bool SkipLiterals
    {
        get => _skipLiterals;
        set
        {
            if (value == _skipLiterals) return;

            _skipLiterals = value;
            if (_provider is not null) _provider.SkipLiterals = value;
        }
    }

    [AllowNull]
    public override string SelectedText
    {
        get => base.SelectedText;
        set => ReplaceSelectionWithMaskedInput(value ?? string.Empty);
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

    private protected override float AlignmentOffset(float lineWidth, int areaWidth) => _textAlign switch
    {
        HorizontalAlignment.Right => Math.Max(areaWidth - lineWidth - 1, 0),
        HorizontalAlignment.Center => Math.Max((areaWidth - lineWidth) / 2, 0),
        _ => 0,
    };

    [Browsable(false)]
    [DefaultValue(null)]
    public Type? ValidatingType
    {
        get => _validatingType;
        set => _validatingType = value;
    }

    public event EventHandler? MaskChanged;

    public event MaskInputRejectedEventHandler? MaskInputRejected;

    public event EventHandler? IsOverwriteModeChanged;

    public event EventHandler? TextAlignChanged;

    public event TypeValidationEventHandler? TypeValidationCompleted;

    protected virtual void OnMaskChanged(EventArgs e) => MaskChanged?.Invoke(this, e);

    protected virtual void OnIsOverwriteModeChanged(EventArgs e) => IsOverwriteModeChanged?.Invoke(this, e);

    protected virtual void OnTextAlignChanged(EventArgs e) => TextAlignChanged?.Invoke(this, e);

    /// <summary>Converts <see cref="Text"/> to <see cref="ValidatingType"/> and raises <see cref="TypeValidationCompleted"/>; returns the converted value, or the text itself when there is nothing to convert to or the conversion failed.</summary>
    public object ValidateText() => RunTypeValidation().ReturnValue ?? Text;

    /// <summary>Always false: a masked box keeps no undo history.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool CanUndo => false;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public new void ClearUndo()
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public new void Undo()
    {
    }

    public override string ToString() => $"{base.ToString()}, Mask: {_mask}";
}
