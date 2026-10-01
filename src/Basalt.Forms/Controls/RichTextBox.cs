using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Text;
using System.Windows.Forms.Internal.RichText;

namespace System.Windows.Forms;

/// <summary>
/// A text box whose content carries its own character and paragraph
/// formatting: bold, italic, colour, alignment, indents and bullets, saved
/// and loaded as RTF. TextBoxBase still owns the plain text, the caret and
/// the selection positions; this class keeps a parallel <see cref="RichDocument"/>
/// of formatting in step with it and replaces the plain editor's painting
/// and hit testing with one that knows about runs.
/// </summary>
/// <remarks>
/// Left out, as the contract asks: OLE objects, tables and embedded images
/// in RTF or in the document model. <see cref="CanPaste(DataFormats.Format)"/>
/// and <see cref="Paste(DataFormats.Format)"/> are left out too: they need a
/// <c>DataFormats</c> type this codebase does not have yet.
/// </remarks>
[DefaultEvent("TextChanged")]
public partial class RichTextBox : TextBoxBase
{
    private RichDocument _document;
    private readonly RichLayout _layout = new();
    private bool _layoutDirty = true;
    private int _laidOutWidth = -1;

    /// <summary>The plain text TextBoxBase's inherited editor held the last time the document was in step with it.</summary>
    private string _syncedPlainText = string.Empty;

    /// <summary>True while this class is itself assigning base.Text to replay a snapshot: the usual diff-based sync must not run.</summary>
    private bool _suppressSync;

    /// <summary>The format set via a Selection* property while nothing is selected: what the next typed character takes.</summary>
    private CharacterFormat? _insertionFormat;

    private float _scrollX;
    private float _scrollY;
    private bool _selectingWithMouse;

    private readonly List<UndoSnapshot> _undoStack = [];
    private readonly List<UndoSnapshot> _redoStack = [];

    private List<(int Start, int Length)> _links = [];

    private bool _autoWordSelection;
    private bool _detectUrls = true;
    private bool _enableAutoDragDrop;
    private RichTextBoxLanguageOptions _languageOption;
    private bool _richTextShortcutsEnabled = true;
    private int _rightMargin;
    private RichTextBoxScrollBars _scrollBars = RichTextBoxScrollBars.Both;
    private bool _showSelectionMargin;
    private float _zoomFactor = 1f;
    private int _bulletIndent;

    public RichTextBox()
    {
        _document = new RichDocument(CharacterFormat.FromFont(Font, ForeColor));

        // A plain TextBoxBase starts single-line and 32767 characters; RichTextBox's own defaults are wider.
        Multiline = true;
        AutoSize = false;
        MaxLength = int.MaxValue;

        TextChanged += OnBaseTextChanged;
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

    [DefaultValue(false)]
    public bool AutoWordSelection
    {
        get => _autoWordSelection;
        set => _autoWordSelection = value;
    }

    [DefaultValue(0)]
    [Localizable(true)]
    public int BulletIndent
    {
        get => _bulletIndent;
        set
        {
            if (value == _bulletIndent) return;

            _bulletIndent = value;
            InvalidateLayout();
        }
    }

    [Browsable(false)]
    public bool CanRedo => _redoStack.Count > 0;

    protected override Size DefaultSize => new(100, 96);

    [DefaultValue(true)]
    public bool DetectUrls
    {
        get => _detectUrls;
        set
        {
            if (value == _detectUrls) return;

            _detectUrls = value;
            RebuildLinks();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool EnableAutoDragDrop
    {
        get => _enableAutoDragDrop;
        // Drag and drop has no counterpart in this codebase yet (Control has no AllowDrop); stored for API compatibility.
        set => _enableAutoDragDrop = value;
    }

    public override Font Font
    {
        get => base.Font;
        set
        {
            base.Font = value;
            _document.DefaultFormat = _document.DefaultFormat.WithFont(value);
            InvalidateLayout();
        }
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            _document.DefaultFormat = _document.DefaultFormat with { Color = value };
            InvalidateLayout();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RichTextBoxLanguageOptions LanguageOption
    {
        get => _languageOption;
        set => _languageOption = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RedoActionName => _redoStack.Count > 0 ? "Typing" : string.Empty;

    [DefaultValue(true)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool RichTextShortcutsEnabled
    {
        get => _richTextShortcutsEnabled;
        set => _richTextShortcutsEnabled = value;
    }

    [DefaultValue(0)]
    [Localizable(true)]
    public int RightMargin
    {
        get => _rightMargin;
        set
        {
            if (value == _rightMargin) return;

            _rightMargin = value;
            InvalidateLayout();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [RefreshProperties(RefreshProperties.All)]
    [AllowNull]
    public string Rtf
    {
        get => RtfWriter.Write(_document);
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                Clear();
                return;
            }

            PushUndo();

            var parsed = RtfReader.Read(value, _document.DefaultFormat);
            _document.ReplaceWith(parsed);
            ReplaceBaseTextFromDocument();
            Select(0, 0);
            base.OnTextChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(RichTextBoxScrollBars.Both)]
    [Localizable(true)]
    public RichTextBoxScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(RichTextBoxScrollBars));
            if (value == _scrollBars) return;

            _scrollBars = value;
            UpdateStyles();
        }
    }

    [DefaultValue("")]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public string SelectedRtf
    {
        get => RtfWriter.WriteRange(_document, SelectionStart, SelectionLength);
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            if (_document.AnyProtected(SelectionStart, SelectionLength)) { OnProtected(EventArgs.Empty); return; }

            PushUndo();

            var insertAt = SelectionStart;
            var fragment = RtfReader.Read(value, _document.FormatBeforeCaret(insertAt));
            if (SelectionLength > 0) _document.Remove(SelectionStart, SelectionLength);

            var insertedLength = fragment.Length;
            _document.InsertFragment(insertAt, fragment);
            ReplaceBaseTextFromDocument();
            Select(insertAt + insertedLength, 0);
            base.OnTextChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(HorizontalAlignment.Left)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HorizontalAlignment SelectionAlignment
    {
        get => _document.CommonParagraphValue(SelectionStart, SelectionLength, p => p.Alignment) ?? default;
        set => ApplyParagraphFormat(p => p.Alignment = value);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectionBackColor
    {
        get => SelectionLength == 0
            ? (_insertionFormat ?? _document.FormatBeforeCaret(SelectionStart)).BackColor
            : _document.CommonCharacterValue(SelectionStart, SelectionLength, f => f.BackColor) ?? Color.Empty;
        set => ApplyCharacterFormat(f => f with { BackColor = value });
    }

    [DefaultValue(false)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SelectionBullet
    {
        get => _document.CommonParagraphValue(SelectionStart, SelectionLength, p => p.Bullet) ?? false;
        set => ApplyParagraphFormat(p => p.Bullet = value);
    }

    [DefaultValue(0)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionCharOffset
    {
        get => SelectionLength == 0
            ? (_insertionFormat ?? _document.FormatBeforeCaret(SelectionStart)).CharOffset
            : _document.CommonCharacterValue(SelectionStart, SelectionLength, f => f.CharOffset) ?? 0;
        set => ApplyCharacterFormat(f => f with { CharOffset = value });
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectionColor
    {
        get => SelectionLength == 0
            ? (_insertionFormat ?? _document.FormatBeforeCaret(SelectionStart)).Color
            : _document.CommonCharacterValue(SelectionStart, SelectionLength, f => f.Color) ?? Color.Empty;
        set => ApplyCharacterFormat(f => f with { Color = value });
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Font? SelectionFont
    {
        get
        {
            if (SelectionLength == 0) return (_insertionFormat ?? _document.FormatBeforeCaret(SelectionStart)).ToFont();

            var common = _document.CommonCharacterValue(SelectionStart, SelectionLength, FontKey.Of);
            return common?.ToFont();
        }
        set
        {
            if (value is null) return;

            ApplyCharacterFormat(f => f.WithFont(value));
        }
    }

    [DefaultValue(0)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionHangingIndent
    {
        get => _document.CommonParagraphValue(SelectionStart, SelectionLength, p => p.HangingIndent) ?? 0;
        set => ApplyParagraphFormat(p => p.HangingIndent = Math.Max(value, 0));
    }

    [DefaultValue(0)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionIndent
    {
        get => _document.CommonParagraphValue(SelectionStart, SelectionLength, p => p.Indent) ?? 0;
        set => ApplyParagraphFormat(p => p.Indent = Math.Max(value, 0));
    }

    public override int SelectionLength
    {
        get => base.SelectionLength;
        // TextBoxBase reports the change through SelectionChangedInternal, which raises SelectionChanged.
        set => base.SelectionLength = value;
    }

    [DefaultValue(false)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SelectionProtected
    {
        get => _document.CommonCharacterValue(SelectionStart, SelectionLength, f => f.Protected) ?? false;
        set => ApplyCharacterFormat(f => f with { Protected = value });
    }

    [DefaultValue(0)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionRightIndent
    {
        get => _document.CommonParagraphValue(SelectionStart, SelectionLength, p => p.RightIndent) ?? 0;
        set => ApplyParagraphFormat(p => p.RightIndent = Math.Max(value, 0));
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int[] SelectionTabs
    {
        get => _document.GetParagraphFormatAt(SelectionStart).Tabs;
        set => ApplyParagraphFormat(p => p.Tabs = value is null ? [] : [.. value]);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RichTextBoxSelectionTypes SelectionType => SelectionLength == 0 ? RichTextBoxSelectionTypes.Empty : RichTextBoxSelectionTypes.Text;

    [DefaultValue(false)]
    public bool ShowSelectionMargin
    {
        get => _showSelectionMargin;
        set
        {
            if (value == _showSelectionMargin) return;

            _showSelectionMargin = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    public override int TextLength => _document.Length;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string UndoActionName => _undoStack.Count > 0 ? "Typing" : string.Empty;

    [DefaultValue(1f)]
    [Localizable(true)]
    public float ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            if (value is <= 0.1f or > 64f) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _zoomFactor) return;

            _zoomFactor = value;
            InvalidateLayout();
        }
    }

    public event ContentsResizedEventHandler? ContentsResized;

    public event EventHandler? HScroll;

    public event EventHandler? ImeChange;

    public event LinkClickedEventHandler? LinkClicked;

    public event EventHandler? Protected;

    public event EventHandler? SelectionChanged;

    public event EventHandler? VScroll;

    // CanPaste(DataFormats.Format) and Paste(DataFormats.Format) are left out: System.Windows.Forms.DataFormats
    // (and its nested Format type) does not exist yet anywhere in this assembly, so their signatures cannot even
    // be declared. See the report for the dependency this leaves on whoever adds DataFormats/Clipboard formats.

    protected virtual void OnContentsResized(ContentsResizedEventArgs e) => ContentsResized?.Invoke(this, e);

    protected virtual void OnHScroll(EventArgs e) => HScroll?.Invoke(this, e);

    protected virtual void OnImeChange(EventArgs e) => ImeChange?.Invoke(this, e);

    protected virtual void OnLinkClicked(LinkClickedEventArgs e) => LinkClicked?.Invoke(this, e);

    protected virtual void OnProtected(EventArgs e) => Protected?.Invoke(this, e);

    protected virtual void OnSelectionChanged(EventArgs e) => SelectionChanged?.Invoke(this, e);

    protected virtual void OnVScroll(EventArgs e) => VScroll?.Invoke(this, e);
}
