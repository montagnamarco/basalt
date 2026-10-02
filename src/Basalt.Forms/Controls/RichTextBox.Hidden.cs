using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Members RichTextBox re-declares only to keep the public shape Microsoft
/// declares on this type: most forward to TextBoxBase/Control, since this
/// implementation keeps its content as plain text with character and
/// paragraph formatting rather than hosting a native rich edit control.
/// </summary>
/// <remarks>
/// WndProc is skipped (a Control member p0-control.txt lists as not yet
/// implemented). CreateRichEditOleCallback throws: it hands out a COM
/// IRichEditOleCallback for the native RICHEDIT window's OLE drag-drop,
/// and there is no native window here for it to serve.
/// </remarks>
public partial class RichTextBox
{
    private const string NoNativeOleCallbackMessage =
        "CreateRichEditOleCallback serves the native RICHEDIT control's OLE drag-drop; this implementation has no native window for it to serve.";

    /// <summary>Whether the clipboard has anything to paste; every text format pastes the same visible text in this implementation.</summary>
    public bool CanPaste(DataFormats.Format format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return !string.IsNullOrEmpty(PlatformClipboard.GetText());
    }

    /// <summary>Pastes the clipboard's text regardless of the requested format: this control keeps one plain-text-with-formatting document, not separate per-format content.</summary>
    public void Paste(DataFormats.Format format)
    {
        ArgumentNullException.ThrowIfNull(format);

        Paste();
    }

    /// <summary>Hides Control.DrawToBitmap only to keep this type's declared shape; ordinary managed painting already renders correctly off-screen.</summary>
    public new void DrawToBitmap(Bitmap bitmap, Rectangle targetBounds) => base.DrawToBitmap(bitmap, targetBounds);

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected virtual object CreateRichEditOleCallback() => throw new PlatformNotSupportedException(NoNativeOleCallbackMessage);

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => base.ProcessCmdKey(ref msg, keyData);

    public override bool AllowDrop
    {
        get => base.AllowDrop;
        set => base.AllowDrop = value;
    }

    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    public override int MaxLength
    {
        get => base.MaxLength;
        set => base.MaxLength = value;
    }

    public override bool Multiline
    {
        get => base.Multiline;
        set => base.Multiline = value;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
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
    public new event DragEventHandler? DragDrop
    {
        add => base.DragDrop += value;
        remove => base.DragDrop -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DragEventHandler? DragEnter
    {
        add => base.DragEnter += value;
        remove => base.DragEnter -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DragLeave
    {
        add => base.DragLeave += value;
        remove => base.DragLeave -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DragEventHandler? DragOver
    {
        add => base.DragOver += value;
        remove => base.DragOver -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event GiveFeedbackEventHandler? GiveFeedback
    {
        add => base.GiveFeedback += value;
        remove => base.GiveFeedback -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event QueryContinueDragEventHandler? QueryContinueDrag
    {
        add => base.QueryContinueDrag += value;
        remove => base.QueryContinueDrag -= value;
    }
}
