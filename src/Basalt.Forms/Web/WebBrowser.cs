using System.ComponentModel;
using System.Drawing;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// Hosts the platform browser through Avalonia when the WinForms control is
/// attached to a visual host. Its in-memory navigation model also supports
/// controls used before a visual host exists.
/// </summary>
public partial class WebBrowser : WebBrowserBase
{
    private const string IeOnly = "This Internet Explorer or ActiveX feature has no equivalent in the native WebView.";

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
    private NativeWebView? _nativeView;
    private Uri? _requestedUrl;
    private bool _ignoreNativeCompletion;
    private bool _nativeCanGoBack;
    private bool _nativeCanGoForward;
    private long _nativeRequestToken;
    private long _domGeneration;
    private HtmlDocument? _domDocument;
    private bool _domReady;

    internal long DomGeneration => _domGeneration;
    internal TimeSpan DomScriptTimeout { get; set; } = TimeSpan.FromSeconds(5);
    internal Func<string, Task<string?>>? DomScriptEvaluator { get; set; }

    internal Task<string?> EvaluateDomScript(string script) =>
        DomScriptEvaluator is { } evaluator ? evaluator(script) : _nativeView!.InvokeScript(script);

    internal void CompleteDomDocumentForTests(Func<string, Task<string?>> evaluator)
    {
        DomScriptEvaluator = evaluator;
        _domGeneration++;
        _domReady = true;
        _domDocument = new HtmlDocument(new NativeDomBridge(this, _domGeneration));
    }

    private void InvalidateDomDocument()
    {
        _domGeneration++;
        _domReady = false;
        _domDocument = null;
    }

    /// <summary>The browser surface mounted by the FormsCanvas overlay.</summary>
    internal Avalonia.Controls.Control NativeView => _nativeView ?? CreateNativeView();

    // The engine normally destroys its platform view when its Avalonia visual
    // leaves a window. A control-tree move must retain it until the new canvas
    // has attached the same visual, preserving the page and navigation state.
    internal IDisposable? BeginNativeReparenting() => _nativeView?.BeginReparenting(false);

    private NativeWebView CreateNativeView()
    {
        var view = new NativeWebView();
        view.NavigationStarted += NativeNavigationStarted;
        view.NavigationCompleted += NativeNavigationCompleted;
        view.NewWindowRequested += NativeNewWindowRequested;
        view.AdapterCreated += NativeAdapterCreated;
        _nativeView = view;

        // A URL or HTML assigned before the visual host existed now gets a
        // real engine load. Cancel the queued headless completion first.
        _navigationToken++;
        if (_documentText.Length > 0) view.NavigateToString(_documentText, new Uri("about:blank"));
        else if (_requestedUrl is { } pending) view.Source = pending;
        else if (_url is { } current) view.Source = current;

        return view;
    }

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

        // Control fills the window-coloured background until the native
        // browser surface is attached by the hosting canvas.
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
    public bool CanGoBack => _nativeView?.CanGoBack ?? _historyIndex > 0;

    [Browsable(false)]
    public bool CanGoForward => _nativeView?.CanGoForward ?? (_historyIndex >= 0 && _historyIndex < _history.Count - 1);

    /// <summary>The loaded native document, when the WebView engine is ready.</summary>
    [Browsable(false)]
    public HtmlDocument? Document => _domReady ? _domDocument : null;

    [Browsable(false)]
    public Stream? DocumentStream
    {
        get => new MemoryStream(Encoding.UTF8.GetBytes(_documentText));
        set
        {
            if (value is null)
            {
                DocumentText = string.Empty;
                return;
            }

            using var reader = new StreamReader(value, leaveOpen: true);
            DocumentText = reader.ReadToEnd();
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
        set
        {
            InvalidateDomDocument();
            _documentText = value ?? string.Empty;
            _requestedUrl = null;
            _nativeView?.NavigateToString(_documentText, new Uri("about:blank"));
        }
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
    /// The native WebView exposes no browser-wide offline mode flag.
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
        get => null;
        set
        {
            if (value is not null) throw new PlatformNotSupportedException(IeOnly);
        }
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

    /// <summary>The native view does not expose an equivalent status string.</summary>
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

    protected override void Dispose(bool disposing)
    {
        InvalidateDomDocument();
        if (disposing && _nativeView is { } view)
        {
            view.NavigationStarted -= NativeNavigationStarted;
            view.NavigationCompleted -= NativeNavigationCompleted;
            view.NewWindowRequested -= NativeNewWindowRequested;
            view.AdapterCreated -= NativeAdapterCreated;
            _nativeView = null;
        }

        base.Dispose(disposing);
    }

    public bool GoBack()
    {
        if (_nativeView is { } view) return view.GoBack();
        if (!CanGoBack) return false;

        BeginNavigate(_history[_historyIndex - 1], null, NavigationKind.Back);
        return true;
    }

    public bool GoForward()
    {
        if (_nativeView is { } view) return view.GoForward();
        if (!CanGoForward) return false;

        BeginNavigate(_history[_historyIndex + 1], null, NavigationKind.Forward);
        return true;
    }

    /// <summary>No platform-wide home page is exposed by the native engine.</summary>
    public void GoHome() => Navigate(new Uri("about:blank"));

    /// <summary>No platform-wide search provider is exposed by the native engine.</summary>
    public void GoSearch() => Navigate(new Uri("about:blank"));

    public void Navigate(string urlString) => Navigate(ResolveUrl(urlString), null, null, null);

    public void Navigate(string urlString, bool newWindow)
    {
        if (newWindow) throw new PlatformNotSupportedException(IeOnly);
        Navigate(urlString);
    }

    public void Navigate(string urlString, string? targetFrameName) => Navigate(ResolveUrl(urlString), targetFrameName, null, null);

    public void Navigate(string urlString, string? targetFrameName, byte[]? postData, string? additionalHeaders) =>
        Navigate(ResolveUrl(urlString), targetFrameName, postData, additionalHeaders);

    public void Navigate(Uri url) => Navigate(url, null, null, null);

    public void Navigate(Uri url, bool newWindow)
    {
        if (newWindow) throw new PlatformNotSupportedException(IeOnly);
        Navigate(url);
    }

    public void Navigate(Uri url, string? targetFrameName) => Navigate(url, targetFrameName, null, null);

    public void Navigate(Uri url, string? targetFrameName, byte[]? postData, string? additionalHeaders)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!string.IsNullOrEmpty(targetFrameName) || postData is { Length: > 0 } || !string.IsNullOrEmpty(additionalHeaders))
            throw new PlatformNotSupportedException(IeOnly);
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
        if (_nativeView is { } view)
        {
            view.Refresh();
            return;
        }
        if (_url is { } current) BeginNavigate(current, null, NavigationKind.Refresh);
    }

    public void Refresh(WebBrowserRefreshOption opt) => Refresh();

    /// <summary>Page setup is part of IE's print dialog, not the native WebView API.</summary>
    public void ShowPageSetupDialog()
    {
        throw new PlatformNotSupportedException(IeOnly);
    }

    /// <summary>Opens the native browser's print interface when hosted.</summary>
    public void ShowPrintDialog()
    {
        _nativeView?.ShowPrintUI();
    }

    /// <summary>The native WebView exposes print UI but no IE preview dialog.</summary>
    public void ShowPrintPreviewDialog()
    {
        throw new PlatformNotSupportedException(IeOnly);
    }

    /// <summary>IE's ActiveX properties dialog has no native WebView equivalent.</summary>
    public void ShowPropertiesDialog()
    {
        throw new PlatformNotSupportedException(IeOnly);
    }

    /// <summary>IE's Save As dialog has no native WebView equivalent.</summary>
    public void ShowSaveAsDialog()
    {
        throw new PlatformNotSupportedException(IeOnly);
    }

    public void Stop()
    {
        if (_nativeView is { } view)
        {
            _nativeRequestToken++;
            view.Stop();
            _ignoreNativeCompletion = true;
        }
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
        if (!_allowNavigation && (_nativeView is not null ? _url is not null : _history.Count > 0)) return;

        _requestedUrl = url;
        if (_nativeView is { } view)
        {
            InvalidateDomDocument();
            _nativeRequestToken++;
            _documentText = string.Empty;
            _isBusy = true;
            _readyState = WebBrowserReadyState.Loading;
            view.Source = url;
            return;
        }

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

    private void NativeNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_nativeView is null) return;

        _ignoreNativeCompletion = false;
        var requestToken = _nativeRequestToken;
        var url = _requestedUrl ?? _nativeView.Source ?? new Uri("about:blank");
        if (!_allowNavigation && _url is not null)
        {
            e.Cancel = true;
            _requestedUrl = null;
            return;
        }

        var navigating = new WebBrowserNavigatingEventArgs(url, null);
        OnNavigating(navigating);
        if (requestToken != _nativeRequestToken)
        {
            e.Cancel = true;
            return;
        }
        if (navigating.Cancel)
        {
            e.Cancel = true;
            _requestedUrl = null;
            _isBusy = false;
            _readyState = _url is null ? WebBrowserReadyState.Uninitialized : WebBrowserReadyState.Complete;
            return;
        }

        InvalidateDomDocument();
        _isBusy = true;
        _readyState = WebBrowserReadyState.Loading;
    }

    private void NativeAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        if (_nativeView is { } view && _documentText.Length > 0)
            view.NavigateToString(_documentText, new Uri("about:blank"));
    }

    private void NativeNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (_nativeView is null || _ignoreNativeCompletion) return;

        _isBusy = false;
        _readyState = e.IsSuccess ? WebBrowserReadyState.Complete : WebBrowserReadyState.Uninitialized;
        if (!e.IsSuccess) return;

        var url = _nativeView.Source ?? _requestedUrl ?? new Uri("about:blank");
        _url = url;
        _requestedUrl = null;
        _domReady = true;
        _domDocument = new HtmlDocument(new NativeDomBridge(this, _domGeneration));
        OnNavigated(new WebBrowserNavigatedEventArgs(url));
        OnDocumentCompleted(new WebBrowserDocumentCompletedEventArgs(url));
        UpdateNativeHistoryState();
        _ = ReadNativeDocumentTitleAsync(_nativeView, url);
    }

    private void NativeNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        var requested = new CancelEventArgs();
        OnNewWindow(requested);
        e.Handled = requested.Cancel;
    }

    private void UpdateNativeHistoryState()
    {
        if (_nativeView is null) return;

        var canGoBack = _nativeView.CanGoBack;
        var canGoForward = _nativeView.CanGoForward;
        if (canGoBack != _nativeCanGoBack) OnCanGoBackChanged(EventArgs.Empty);
        if (canGoForward != _nativeCanGoForward) OnCanGoForwardChanged(EventArgs.Empty);
        _nativeCanGoBack = canGoBack;
        _nativeCanGoForward = canGoForward;
    }

    private async Task ReadNativeDocumentTitleAsync(NativeWebView view, Uri pageUrl)
    {
        try
        {
            var title = await view.InvokeScript("document.title");
            Dispatcher.UIThread.Post(() =>
            {
                if (IsDisposed || !ReferenceEquals(_nativeView, view) || _url != pageUrl || title == _documentTitle) return;

                _documentTitle = title ?? string.Empty;
                OnDocumentTitleChanged(EventArgs.Empty);
            });
        }
        catch (Exception)
        {
            // Some platform adapters do not expose script evaluation until
            // their page is fully initialized; navigation itself still worked.
        }
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
        _requestedUrl = null;
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
