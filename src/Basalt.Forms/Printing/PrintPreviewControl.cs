using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;

namespace System.Windows.Forms;

/// <summary>
/// Shows the pages a <see cref="PrintDocument"/> would print, laid out in a
/// grid of <see cref="Columns"/> by <see cref="Rows"/> pages starting at
/// <see cref="StartPage"/>, over a grey workspace background.
/// </summary>
[DefaultProperty("Document")]
public class PrintPreviewControl : Control
{
    // The gap, in screen pixels and never scaled by Zoom, left around and
    // between pages: Windows draws a fixed margin regardless of zoom level.
    private const int Spacing = 10;

    // How far a page's shadow falls to its bottom-right, in screen pixels.
    private const int ShadowOffset = 4;

    private PrintDocument? _document;
    private PreviewPageInfo[]? _pages;

    private const string PrintFailedMessage = "An error occurred while printing the document.";

    /// <summary>The document threw while rendering: the control says so instead of failing its paint.</summary>
    private bool _printFailed;
    private bool _autoZoom = true;
    private int _columns = 1;
    private int _rows = 1;
    private int _startPage;
    private double _zoom = 0.3;
    private bool _useAntiAlias;

    public PrintPreviewControl()
    {
        SetStyle(ControlStyles.Selectable, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        base.TabStop = false;
        BackColor = SystemColors.AppWorkspace;
    }

    [DefaultValue(true)]
    public bool AutoZoom
    {
        get => _autoZoom;
        set
        {
            if (_autoZoom == value) return;

            _autoZoom = value;
            Invalidate();
        }
    }

    [DefaultValue(1)]
    public int Columns
    {
        get => _columns;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            if (_columns == value) return;

            _columns = value;
            Invalidate();
        }
    }

    [DefaultValue(null)]
    public PrintDocument? Document
    {
        get => _document;
        set
        {
            if (_document == value) return;

            _document = value;
            InvalidatePreview();
        }
    }

    /// <summary>Re-declared only to carry its own ambient-value attribute; the behaviour is the base control's.</summary>
    public override RightToLeft RightToLeft
    {
        get => base.RightToLeft;
        set => base.RightToLeft = value;
    }

    [DefaultValue(1)]
    public int Rows
    {
        get => _rows;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            if (_rows == value) return;

            _rows = value;
            Invalidate();
        }
    }

    [DefaultValue(0)]
    public int StartPage
    {
        get => _startPage;
        set
        {
            EnsurePages();

            var clamped = ClampStartPage(value);
            if (_startPage == clamped) return;

            _startPage = clamped;
            Invalidate();
            OnStartPageChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    [DefaultValue(false)]
    public bool UseAntiAlias
    {
        get => _useAntiAlias;
        set
        {
            if (_useAntiAlias == value) return;

            _useAntiAlias = value;

            // Antialiasing is a property of how the document was rendered to
            // its preview bitmaps, not of how this control paints them: a
            // cached page was rendered with the previous setting.
            InvalidatePreview();
        }
    }

    [DefaultValue(0.3)]
    public double Zoom
    {
        get => _zoom;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);

            // An explicit zoom replaces fitting the pages to the control, as WinForms does.
            _autoZoom = false;
            if (_zoom == value) return;

            _zoom = value;
            Invalidate();
        }
    }

    public event EventHandler? StartPageChanged;

    /// <summary>
    /// Re-declared only to hide it from a designer's event list: a preview
    /// control has no visible text, so Microsoft marks this one not browsable.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }

    /// <summary>Re-declared only to keep the public shape Microsoft declares on this type; the accessible object is the base control's.</summary>
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    /// <summary>Forgets the cached pages, so the next paint renders the document again.</summary>
    public void InvalidatePreview()
    {
        ReleasePages();
        Invalidate();
    }

    /// <summary>The rendered pages are bitmaps at page resolution: dispose them rather than wait for the collector.</summary>
    private void ReleasePages()
    {
        if (_pages is not null)
        {
            foreach (var page in _pages) page.Image.Dispose();
        }

        _pages = null;
        _printFailed = false;
    }

    internal override void OnHandleDestroyedInternal()
    {
        base.OnHandleDestroyedInternal();
        ReleasePages();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);

        var graphics = pevent.Graphics;
        graphics.SmoothingMode = UseAntiAlias ? SmoothingMode.AntiAlias : SmoothingMode.None;

        using var backgroundBrush = new SolidBrush(SystemColors.AppWorkspace);
        graphics.FillRectangle(backgroundBrush, ClientRectangle);

        EnsurePages();

        if (_printFailed)
        {
            TextRenderer.DrawText(graphics, PrintFailedMessage, Font, ClientRectangle, SystemColors.ControlText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        if (_pages is null || _pages.Length == 0) return;

        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, Rows);

        var firstPage = _pages[0];
        var pageWidth = firstPage.PhysicalSize.Width;
        var pageHeight = firstPage.PhysicalSize.Height;
        if (pageWidth <= 0 || pageHeight <= 0) return;

        var zoom = AutoZoom ? ComputeAutoZoom(columns, rows, pageWidth, pageHeight) : Zoom;

        var displayWidth = Math.Max(1, (int)Math.Round(pageWidth * zoom));
        var displayHeight = Math.Max(1, (int)Math.Round(pageHeight * zoom));

        var gridWidth = columns * displayWidth + (columns + 1) * Spacing;
        var gridHeight = rows * displayHeight + (rows + 1) * Spacing;

        var originX = Math.Max(Spacing, (ClientSize.Width - gridWidth) / 2);
        var originY = Math.Max(Spacing, (ClientSize.Height - gridHeight) / 2);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = StartPage + row * columns + column;
                if (index < 0 || index >= _pages.Length) continue;

                var x = originX + column * (displayWidth + Spacing);
                var y = originY + row * (displayHeight + Spacing);

                DrawPage(graphics, _pages[index], x, y, displayWidth, displayHeight);
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The grey workspace is painted as part of OnPaint, together with the
        // pages: painting it here too would only waste a fill.
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }

    protected virtual void OnStartPageChanged(EventArgs e) => StartPageChanged?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void ResetBackColor() => BackColor = SystemColors.AppWorkspace;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void ResetForeColor() => ForeColor = Color.Empty;

    /// <summary>Draws one page: a falling shadow, the white sheet, its image, and a 1-pixel black frame.</summary>
    private static void DrawPage(Graphics graphics, PreviewPageInfo page, int x, int y, int width, int height)
    {
        using var shadowBrush = new SolidBrush(SystemColors.ControlDark);
        graphics.FillRectangle(shadowBrush, x + ShadowOffset, y + ShadowOffset, width, height);

        using var pageBrush = new SolidBrush(Color.White);
        graphics.FillRectangle(pageBrush, x, y, width, height);

        graphics.DrawImage(page.Image, new Rectangle(x, y, width, height));

        using var framePen = new Pen(Color.Black);
        graphics.DrawRectangle(framePen, x, y, width - 1, height - 1);
    }

    /// <summary>Renders every page once through a PreviewPrintController, caching the result.</summary>
    private void EnsurePages()
    {
        if (_pages is not null) return;

        if (_document is null)
        {
            _pages = [];
            return;
        }

        var controller = new PreviewPrintController { UseAntiAlias = UseAntiAlias };
        var previousController = _document.PrintController;

        _document.PrintController = controller;
        try
        {
            _document.Print();
            _pages = controller.GetPreviewPageInfo();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // As WinForms: a PrintPage handler that throws leaves a message in the preview, not a broken paint.
            _pages = [];
            _printFailed = true;
        }
        finally
        {
            _document.PrintController = previousController;
        }

        _startPage = ClampStartPage(_startPage);
    }

    private int ClampStartPage(int value)
    {
        var maxPage = _pages is { Length: > 0 } ? _pages.Length - 1 : 0;

        return Math.Clamp(value, 0, maxPage);
    }

    /// <summary>The zoom that fits Columns by Rows pages into the client area, each page keeping its own aspect ratio.</summary>
    private double ComputeAutoZoom(int columns, int rows, int pageWidth, int pageHeight)
    {
        var availableWidth = ClientSize.Width - (columns + 1) * Spacing;
        var availableHeight = ClientSize.Height - (rows + 1) * Spacing;
        if (availableWidth <= 0 || availableHeight <= 0) return 0.01;

        var widthZoom = (double)availableWidth / columns / pageWidth;
        var heightZoom = (double)availableHeight / rows / pageHeight;

        return Math.Max(0.01, Math.Min(widthZoom, heightZoom));
    }
}
