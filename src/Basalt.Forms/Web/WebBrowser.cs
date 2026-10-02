using System.ComponentModel;
using System.Drawing;
using System.Text;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// A browser that always shows a blank page. Windows' WebBrowser hosts
/// Internet Explorer's ActiveX control, which fetches and renders real
/// HTML; Basalt has no HTML engine, so this keeps everything a real
/// WebBrowser keeps - the navigation history, Navigating/Navigated/
/// DocumentCompleted in the same order, CanGoBack/CanGoForward tracking the
/// history, the asynchronous delivery through the UI message loop a real
/// COM event would also arrive through - and paints the one thing it
/// actually has: an empty page, the colour of a window.
/// </summary>
public class WebBrowser : WebBrowserBase
{
    private const string NoDocument =
        "There is no HTML engine behind this control, so there is no Document to return; DocumentText and DocumentStream hold the content instead.";

    private enum NavigationKind
    {
        New,
        Back,
        Forward,
        Refresh,
    }

    private readonly List<Uri> _history = [];
    private int _historyIndex = -1;
    private long _navigationToken;
    private Uri? _url;
    private string _documentText = string.Empty;
    private string _documentTitle = string.Empty;
    private WebBrowserReadyState _readyState = WebBrowserReadyState.Uninitialized;
    private bool _isBusy;
    private bool _allowNavigation = true;
    private bool _allowWebBrowserDrop = true;
    private bool _isWebBrowserContextMenuEnabled = true;
    private bool _webBrowserShortcutsEnabled = true;
    private bool _scriptErrorsSuppressed;
    private bool _scrollBarsEnabled = true;
    private object? _objectForScripting;

    private static readonly object s_canGoBackChangedEvent = new();
    private static readonly object s_canGoForwardChangedEvent = new();
    private static readonly object s_documentCompletedEvent = new();
    private static readonly object s_documentTitleChangedEvent = new();
    private static readonly object s_encryptionLevelChangedEvent = new();
    private static readonly object s_fileDownloadEvent = new();
    private static readonly object s_navigatedEvent = new();
    private static readonly object s_navigatingEvent = new();
    private static readonly object s_newWindowEvent = new();
    private static readonly object s_progressChangedEvent = new();
    private static readonly object s_statusTextChangedEvent = new();

    public WebBrowser()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);

        // The one thing a real WebBrowser shows before (or instead of) real
        // content: the window-coloured background its ActiveX surface
        // paints. Control already fills with BackColor before OnPaint runs,
        // so there is no need to override OnPaint - WebBrowser does not, in
        // the real assembly either - just to start with that colour.
        BackColor = SystemColors.Window;
    }

    protected override Size DefaultSize => new(250, 250);

    [Browsable(false)]
    [DefaultValue(true)]
    public bool AllowNavigation
    {
        get => _allowNavigation;
        set => _allowNavigation = value;
    }

    [Browsable(false)]
    [DefaultValue(true)]
    public bool AllowWebBrowserDrop
    {
        get => _allowWebBrowserDrop;
        set => _allowWebBrowserDrop = value;
    }

    [Browsable(false)]
    public bool CanGoBack => _historyIndex > 0;

    [Browsable(false)]
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    /// <summary>Always null: there is no HTML DOM for a document to wrap.</summary>
    [Browsable(false)]
    public HtmlDocument? Document => null;

    [Browsable(false)]
    public Stream? DocumentStream
    {
        get => new MemoryStream(Encoding.UTF8.GetBytes(_documentText));
        set
        {
            if (value is null)
            {
                _documentText = string.Empty;
                return;
            }

            using var reader = new StreamReader(value, leaveOpen: true);
            _documentText = reader.ReadToEnd();
        }
    }

    [Browsable(false)]
    public event EventHandler? CanGoBackChanged
    {
        add => Events.AddHandler(s_canGoBackChangedEvent, value);
        remove => Events.RemoveHandler(s_canGoBackChangedEvent, value);
    }

    [Browsable(false)]
    public event EventHandler? CanGoForwardChanged
    {
        add => Events.AddHandler(s_canGoForwardChangedEvent, value);
        remove => Events.RemoveHandler(s_canGoForwardChangedEvent, value);
    }

    public event WebBrowserDocumentCompletedEventHandler? DocumentCompleted
    {
        add => Events.AddHandler(s_documentCompletedEvent, value);
        remove => Events.RemoveHandler(s_documentCompletedEvent, value);
    }

    public event EventHandler? DocumentTitleChanged
    {
        add => Events.AddHandler(s_documentTitleChangedEvent, value);
        remove => Events.RemoveHandler(s_documentTitleChangedEvent, value);
    }

    public event EventHandler? EncryptionLevelChanged
    {
        add => Events.AddHandler(s_encryptionLevelChangedEvent, value);
        remove => Events.RemoveHandler(s_encryptionLevelChangedEvent, value);
    }

    public event EventHandler? FileDownload
    {
        add => Events.AddHandler(s_fileDownloadEvent, value);
        remove => Events.RemoveHandler(s_fileDownloadEvent, value);
    }

    public event WebBrowserNavigatedEventHandler? Navigated
    {
        add => Events.AddHandler(s_navigatedEvent, value);
        remove => Events.RemoveHandler(s_navigatedEvent, value);
    }

    public event WebBrowserNavigatingEventHandler? Navigating
    {
        add => Events.AddHandler(s_navigatingEvent, value);
        remove => Events.RemoveHandler(s_navigatingEvent, value);
    }

    public event CancelEventHandler? NewWindow
    {
        add => Events.AddHandler(s_newWindowEvent, value);
        remove => Events.RemoveHandler(s_newWindowEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    public event WebBrowserProgressChangedEventHandler? ProgressChanged
    {
        add => Events.AddHandler(s_progressChangedEvent, value);
        remove => Events.RemoveHandler(s_progressChangedEvent, value);
    }

    public event EventHandler? StatusTextChanged
    {
        add => Events.AddHandler(s_statusTextChangedEvent, value);
        remove => Events.RemoveHandler(s_statusTextChangedEvent, value);
    }

    [Browsable(false)]
    public string DocumentText
    {
        get => _documentText;
        set => _documentText = value ?? string.Empty;
    }

    [Browsable(false)]
    public string DocumentTitle => _documentTitle;

    [Browsable(false)]
    public string DocumentType => string.Empty;

    [Browsable(false)]
    public WebBrowserEncryptionLevel EncryptionLevel => WebBrowserEncryptionLevel.Insecure;

    public override bool Focused => base.Focused;

    [Browsable(false)]
    public bool IsBusy => _isBusy;

    /// <summary>
    /// Whether the ocx's moniker reports it is working offline. There is no
    /// ocx and no network moniker here, so this never reports offline.
    /// </summary>
    [Browsable(false)]
    public bool IsOffline => false;

    [Browsable(false)]
    [DefaultValue(true)]
    public bool IsWebBrowserContextMenuEnabled
    {
        get => _isWebBrowserContextMenuEnabled;
        set => _isWebBrowserContextMenuEnabled = value;
    }

    [Browsable(false)]
    [DefaultValue(null)]
    public object? ObjectForScripting
    {
        get => _objectForScripting;
        set => _objectForScripting = value;
    }

    [Localizable(true)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    public WebBrowserReadyState ReadyState => _readyState;

    [Browsable(false)]
    [DefaultValue(false)]
    public bool ScriptErrorsSuppressed
    {
        get => _scriptErrorsSuppressed;
        set => _scriptErrorsSuppressed = value;
    }

    [Browsable(false)]
    [DefaultValue(true)]
    public bool ScrollBarsEnabled
    {
        get => _scrollBarsEnabled;
        set => _scrollBarsEnabled = value;
    }

    /// <summary>No status bar text off Windows: there is no page load to report progress on.</summary>
    public virtual string StatusText => string.Empty;

    [Browsable(false)]
    public Uri? Url
    {
        get => _url;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            BeginNavigate(value, null, NavigationKind.New);
        }
    }

    [Browsable(false)]
    public Version Version => Environment.Version;

    [Browsable(false)]
    [DefaultValue(true)]
    public bool WebBrowserShortcutsEnabled
    {
        get => _webBrowserShortcutsEnabled;
        set => _webBrowserShortcutsEnabled = value;
    }

    protected override void AttachInterfaces(object nativeActiveXObject)
    {
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateSink()
    {
    }

    protected override WebBrowserSiteBase CreateWebBrowserSiteBase() => base.CreateWebBrowserSiteBase();

    protected override void DetachInterfaces()
    {
    }

    protected override void DetachSink()
    {
    }

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    public bool GoBack()
    {
        if (!CanGoBack) return false;

        BeginNavigate(_history[_historyIndex - 1], null, NavigationKind.Back);
        return true;
    }

    public bool GoForward()
    {
        if (!CanGoForward) return false;

        BeginNavigate(_history[_historyIndex + 1], null, NavigationKind.Forward);
        return true;
    }

    /// <summary>No configured home page off Windows; navigates to the same blank page Basalt always shows.</summary>
    public void GoHome() => Navigate(new Uri("about:home"));

    /// <summary>No configured search provider off Windows; navigates to the same blank page Basalt always shows.</summary>
    public void GoSearch() => Navigate(new Uri("about:search"));

    public void Navigate(string urlString) => Navigate(ResolveUrl(urlString), null, null, null);

    public void Navigate(string urlString, bool newWindow) => Navigate(urlString);

    public void Navigate(string urlString, string? targetFrameName) => Navigate(ResolveUrl(urlString), targetFrameName, null, null);

    public void Navigate(string urlString, string? targetFrameName, byte[]? postData, string? additionalHeaders) =>
        Navigate(ResolveUrl(urlString), targetFrameName, postData, additionalHeaders);

    public void Navigate(Uri url) => Navigate(url, null, null, null);

    public void Navigate(Uri url, bool newWindow) => Navigate(url);

    public void Navigate(Uri url, string? targetFrameName) => Navigate(url, targetFrameName, null, null);

    public void Navigate(Uri url, string? targetFrameName, byte[]? postData, string? additionalHeaders)
    {
        ArgumentNullException.ThrowIfNull(url);
        BeginNavigate(url, targetFrameName, NavigationKind.New);
    }

    private Uri ResolveUrl(string urlString)
    {
        ArgumentNullException.ThrowIfNull(urlString);

        if (Uri.TryCreate(urlString, UriKind.Absolute, out var absolute)) return absolute;
        if (_url is { } current) return new Uri(current, urlString);

        return new Uri(urlString, UriKind.RelativeOrAbsolute);
    }

    protected virtual void OnCanGoBackChanged(EventArgs e) => ((EventHandler?)Events[s_canGoBackChangedEvent])?.Invoke(this, e);

    protected virtual void OnCanGoForwardChanged(EventArgs e) => ((EventHandler?)Events[s_canGoForwardChangedEvent])?.Invoke(this, e);

    protected virtual void OnDocumentCompleted(WebBrowserDocumentCompletedEventArgs e) =>
        ((WebBrowserDocumentCompletedEventHandler?)Events[s_documentCompletedEvent])?.Invoke(this, e);

    protected virtual void OnDocumentTitleChanged(EventArgs e) => ((EventHandler?)Events[s_documentTitleChangedEvent])?.Invoke(this, e);

    protected virtual void OnEncryptionLevelChanged(EventArgs e) => ((EventHandler?)Events[s_encryptionLevelChangedEvent])?.Invoke(this, e);

    protected virtual void OnFileDownload(EventArgs e) => ((EventHandler?)Events[s_fileDownloadEvent])?.Invoke(this, e);

    protected virtual void OnNavigated(WebBrowserNavigatedEventArgs e) =>
        ((WebBrowserNavigatedEventHandler?)Events[s_navigatedEvent])?.Invoke(this, e);

    protected virtual void OnNavigating(WebBrowserNavigatingEventArgs e) =>
        ((WebBrowserNavigatingEventHandler?)Events[s_navigatingEvent])?.Invoke(this, e);

    protected virtual void OnNewWindow(CancelEventArgs e) => ((CancelEventHandler?)Events[s_newWindowEvent])?.Invoke(this, e);

    protected virtual void OnProgressChanged(WebBrowserProgressChangedEventArgs e) =>
        ((WebBrowserProgressChangedEventHandler?)Events[s_progressChangedEvent])?.Invoke(this, e);

    protected virtual void OnStatusTextChanged(EventArgs e) => ((EventHandler?)Events[s_statusTextChangedEvent])?.Invoke(this, e);

    public void Print() => ShowPrintDialog();

    public override void Refresh()
    {
        base.Refresh();
        if (_url is { } current) BeginNavigate(current, null, NavigationKind.Refresh);
    }

    public void Refresh(WebBrowserRefreshOption opt) => Refresh();

    /// <summary>No print pipeline off Windows: there is no document to lay pages out from.</summary>
    public void ShowPageSetupDialog()
    {
    }

    /// <summary>No print pipeline off Windows: there is no document to print.</summary>
    public void ShowPrintDialog()
    {
    }

    /// <summary>No print pipeline off Windows: there is no document to preview.</summary>
    public void ShowPrintPreviewDialog()
    {
    }

    /// <summary>No ActiveX object off Windows to show the properties of.</summary>
    public void ShowPropertiesDialog()
    {
    }

    /// <summary>No document off Windows to save.</summary>
    public void ShowSaveAsDialog()
    {
    }

    public void Stop()
    {
        if (!_isBusy) return;

        // Invalidates whatever CompleteNavigation is already posted for the
        // in-flight navigation: it must not complete behind Stop()'s back,
        // the same way clicking the browser's Stop button on a loading page
        // leaves it without ever finishing.
        _navigationToken++;

        _isBusy = false;
        _readyState = _history.Count > 0 ? WebBrowserReadyState.Complete : WebBrowserReadyState.Uninitialized;
    }

    private void BeginNavigate(Uri url, string? targetFrameName, NavigationKind kind)
    {
        // A real browser still lets the very first navigation through even
        // when it will later be told not to leave that page; AllowNavigation
        // only holds once there is somewhere to stay on.
        if (!_allowNavigation && _history.Count > 0) return;

        _isBusy = true;
        _readyState = WebBrowserReadyState.Loading;

        // A new navigation - or Stop() - always supersedes whatever was
        // already in flight, the same way asking IE to go somewhere else
        // (or pressing Stop) interrupts a page that has not finished
        // loading yet: that stale request must never go on to raise
        // Navigated/DocumentCompleted or reach the history once this one
        // is posted.
        var token = ++_navigationToken;

        // Like a COM event arriving back through the message loop instead of
        // inline with the call that triggered it, so handlers observe the
        // same Navigating/Navigated/DocumentCompleted order a real control
        // would deliver, on the thread that owns this control.
        Dispatcher.UIThread.Post(() => CompleteNavigation(url, targetFrameName, kind, token));
    }

    private void CompleteNavigation(Uri url, string? targetFrameName, NavigationKind kind, long token)
    {
        if (IsDisposed) return;

        // Superseded by a later Navigate/GoBack/GoForward/Refresh, or by
        // Stop(), before this posted step ran. A stale request stops here:
        // it never reaches Navigated/DocumentCompleted and never enters the
        // history, matching an interrupted IE navigation.
        if (token != _navigationToken) return;

        var wasCanGoBack = CanGoBack;
        var wasCanGoForward = CanGoForward;

        var navigating = new WebBrowserNavigatingEventArgs(url, targetFrameName);
        OnNavigating(navigating);

        // A Navigating handler that redirects (Navigate) or calls Stop() has
        // superseded this request: the new state is theirs, leave it alone.
        if (token != _navigationToken) return;

        if (navigating.Cancel)
        {
            _isBusy = false;
            _readyState = _history.Count > 0 ? WebBrowserReadyState.Complete : WebBrowserReadyState.Uninitialized;
            return;
        }

        switch (kind)
        {
            case NavigationKind.New:
                if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(url);
                _historyIndex = _history.Count - 1;
                break;

            case NavigationKind.Back:
                _historyIndex--;
                break;

            case NavigationKind.Forward:
                _historyIndex++;
                break;

            case NavigationKind.Refresh:
                break;
        }

        _url = url;
        var titleChanged = _documentTitle != url.ToString();
        _documentTitle = url.ToString();
        _readyState = WebBrowserReadyState.Complete;
        _isBusy = false;

        OnNavigated(new WebBrowserNavigatedEventArgs(url));
        if (titleChanged) OnDocumentTitleChanged(EventArgs.Empty);
        OnDocumentCompleted(new WebBrowserDocumentCompletedEventArgs(url));

        if (CanGoBack != wasCanGoBack) OnCanGoBackChanged(EventArgs.Empty);
        if (CanGoForward != wasCanGoForward) OnCanGoForwardChanged(EventArgs.Empty);

        Invalidate();
    }
}
