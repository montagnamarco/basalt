using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

/// <summary>
/// Members of Control that answer questions about the platform: product
/// information, lock keys, system colours, mirroring, printing, and the
/// right-to-left translation helpers.
/// </summary>
public partial class Control
{
    private static readonly object s_systemColorsChangedEvent = new();

    private Point _autoScrollOffset;

    /// <summary>Where ScrollControlIntoView should place this control inside a scrolling parent.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual Point AutoScrollOffset
    {
        get => _autoScrollOffset;
        set
        {
            if (_autoScrollOffset == value) return;

            _autoScrollOffset = value;
        }
    }

    /// <summary>The company of the assembly that defines the control's type, else the first part of its namespace.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CompanyName =>
        GetType().Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company is { Length: > 0 } company
            ? company
            : NamespacePart(0);

    /// <summary>The product of the assembly that defines the control's type, else the second part of its namespace.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ProductName =>
        GetType().Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } product
            ? product
            : NamespacePart(1);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ProductVersion =>
        GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? GetType().Assembly.GetName().Version?.ToString()
        ?? "1.0.0.0";

    /// <summary>Whether the window lays itself out from the right, as WS_EX_LAYOUTRTL makes it.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMirrored => (CreateParams.ExStyle & WindowStyles.ExLayoutRtl) != 0;

    /// <summary>Always true in WinForms since .NET 2.0, kept for code that still asks.</summary>
    protected bool RenderRightToLeft => true;

    /// <summary>
    /// Whether a lock key is on. The platform does not report the state of
    /// the lock keys, so it is followed from the presses seen while Basalt
    /// has a window focused, starting from off.
    /// </summary>
    public static bool IsKeyLocked(Keys keyVal)
    {
        if (keyVal is not (Keys.Insert or Keys.NumLock or Keys.CapsLock or Keys.Scroll))
        {
            throw new NotSupportedException("Specified key is not supported. Valid keys are Insert, NumLock, CapsLock, and Scroll.");
        }

        return FormsInput.IsKeyToggled(keyVal);
    }

    /// <summary>
    /// A graphics over the control's client area, for measuring and for code
    /// that draws outside Paint. Basalt composes every window from recorded
    /// paints, so what is drawn on it is not shown: it draws into a bitmap of
    /// the client size that lives as long as the graphics.
    /// </summary>
    public Graphics CreateGraphics()
    {
        var size = ClientSize;
        var bitmap = new Bitmap(Math.Max(size.Width, 1), Math.Max(size.Height, 1));

        // The graphics keeps the bitmap as its target; both go together once the caller drops the graphics.
        return Graphics.FromImage(bitmap);
    }

    /// <summary>Removes every binding of the control, as WinForms' ResetBindings does.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ResetBindings() => _dataBindings?.Clear();

    public event EventHandler? SystemColorsChanged
    {
        add => Events.AddHandler(s_systemColorsChangedEvent, value);
        remove => Events.RemoveHandler(s_systemColorsChangedEvent, value);
    }

    /// <summary>
    /// The system colours changed: children first, then this control
    /// repaints and raises the event. Basalt's skin colours do not change at
    /// run time, so only code that calls it raises it.
    /// </summary>
    protected virtual void OnSystemColorsChanged(EventArgs e)
    {
        foreach (var child in ChildrenSnapshot()) child.OnSystemColorsChanged(EventArgs.Empty);

        Invalidate();
        ((EventHandler?)Events[s_systemColorsChangedEvent])?.Invoke(this, e);
    }

    /// <summary>Paints the control into the graphics it is given, as WM_PRINTCLIENT asks: background and foreground, no children.</summary>
    protected virtual void OnPrint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        PaintClient(e);
    }

    protected void RaiseDragEvent(object key, DragEventArgs e) => ((DragEventHandler?)Events[key])?.Invoke(this, e);

    /// <summary>Arms MouseHover again while the pointer stays over the control.</summary>
    protected void ResetMouseEventArgs() => MouseHoverTracker.Rearm(this);

    /// <summary>The accessible object of a child element by its id; controls with such elements override it.</summary>
    protected virtual AccessibleObject? GetAccessibilityObjectById(int objectId) => null;

    protected HorizontalAlignment RtlTranslateAlignment(HorizontalAlignment align) => RtlTranslateHorizontal(align);

    protected LeftRightAlignment RtlTranslateAlignment(LeftRightAlignment align) => RtlTranslateLeftRight(align);

    protected ContentAlignment RtlTranslateAlignment(ContentAlignment align) => RtlTranslateContent(align);

    protected HorizontalAlignment RtlTranslateHorizontal(HorizontalAlignment align)
    {
        if (RightToLeft != RightToLeft.Yes) return align;

        return align switch
        {
            HorizontalAlignment.Left => HorizontalAlignment.Right,
            HorizontalAlignment.Right => HorizontalAlignment.Left,
            _ => align,
        };
    }

    protected LeftRightAlignment RtlTranslateLeftRight(LeftRightAlignment align)
    {
        if (RightToLeft != RightToLeft.Yes) return align;

        return align == LeftRightAlignment.Left ? LeftRightAlignment.Right : LeftRightAlignment.Left;
    }

    protected internal ContentAlignment RtlTranslateContent(ContentAlignment align)
    {
        if (RightToLeft != RightToLeft.Yes) return align;

        return align switch
        {
            ContentAlignment.TopLeft => ContentAlignment.TopRight,
            ContentAlignment.TopRight => ContentAlignment.TopLeft,
            ContentAlignment.MiddleLeft => ContentAlignment.MiddleRight,
            ContentAlignment.MiddleRight => ContentAlignment.MiddleLeft,
            ContentAlignment.BottomLeft => ContentAlignment.BottomRight,
            ContentAlignment.BottomRight => ContentAlignment.BottomLeft,
            _ => align,
        };
    }

    /// <summary>
    /// The legacy menu of .NET Framework. Microsoft keeps it for binary
    /// compatibility only: ContextMenu cannot be created, so the property
    /// stays empty and setting it does nothing.
    /// </summary>
    [Obsolete("`ContextMenu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ContextMenuStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual ContextMenu? ContextMenu
    {
        get => null;
        set
        {
        }
    }

    [Obsolete("`ContextMenu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ContextMenuStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public event EventHandler? ContextMenuChanged
    {
        add
        {
        }
        remove
        {
        }
    }

    [Obsolete("`ContextMenu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ContextMenuStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void OnContextMenuChanged(EventArgs e)
    {
    }

    private string NamespacePart(int index)
    {
        var parts = (GetType().Namespace ?? string.Empty).Split('.');

        return index < parts.Length ? parts[index] : string.Empty;
    }
}
