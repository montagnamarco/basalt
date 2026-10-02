using System.ComponentModel;
using System.Drawing;
using System.Net.Http;

namespace System.Windows.Forms;

/// <summary>
/// LoadAsync/CancelAsync: the work (reading a file, or fetching an http(s)
/// URL through HttpClient) happens on a background task, never on the UI
/// thread; InitialImage shows while it runs, ErrorImage if it fails, and
/// every callback into the control (progress, completion) is posted back to
/// the UI thread through Avalonia's dispatcher, the same one Control already
/// marshals work through elsewhere (see TaskDialog).
/// </summary>
public partial class PictureBox
{
    private static readonly Lazy<HttpClient> SharedHttpClient = new(() => new HttpClient());

    private CancellationTokenSource? _loadCts;
    private bool _waitOnLoad;

    /// <summary>For tests: replaces the background read, so they can decide when it finishes.</summary>
    internal Func<string, CancellationToken, Image>? FetchOverride { get; set; }

    /// <summary>For tests: completes once the result of the last LoadAsync has been posted to the UI thread.</summary>
    internal Task? PendingDelivery { get; private set; }

    [DefaultValue(false)]
    public bool WaitOnLoad
    {
        get => _waitOnLoad;
        set => _waitOnLoad = value;
    }

    public event ProgressChangedEventHandler? LoadProgressChanged;

    public event AsyncCompletedEventHandler? LoadCompleted;

    protected virtual void OnLoadProgressChanged(ProgressChangedEventArgs e) => LoadProgressChanged?.Invoke(this, e);

    protected virtual void OnLoadCompleted(AsyncCompletedEventArgs e) => LoadCompleted?.Invoke(this, e);

    /// <summary>Starts loading ImageLocation in the background; WaitOnLoad makes it behave like the synchronous Load instead.</summary>
    public void LoadAsync()
    {
        if (string.IsNullOrEmpty(_imageLocation))
        {
            throw new InvalidOperationException("The ImageLocation property has not been set, so an image cannot be loaded.");
        }

        LoadAsync(_imageLocation);
    }

    public void LoadAsync(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Whatever was running is replaced: cancel it, and forget it belongs
        // to us, so its eventual result (success, failure or cancellation)
        // finds no match in PostResult and stays silent - this load, not
        // that one, owns whatever LoadCompleted fires next.
        _loadCts?.Cancel();
        _loadCts = null;

        if (_waitOnLoad)
        {
            Load(url);
            OnLoadProgressChanged(new ProgressChangedEventArgs(100, null));
            OnLoadCompleted(new AsyncCompletedEventArgs(null, false, null));
            return;
        }

        _imageLocation = url;
        if (_initialImage is { } initial) { _image = initial; Invalidate(); }

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        OnLoadProgressChanged(new ProgressChangedEventArgs(0, null));

        var fetch = FetchOverride ?? FetchImage;
        PendingDelivery = Task.Run(() => fetch(url, cts.Token), cts.Token).ContinueWith(task => PostResult(task, cts, url), TaskScheduler.Default);
    }

    /// <summary>
    /// Requests cancellation of the running load. One still in flight ends
    /// with LoadCompleted(Cancelled: true, Error: null) and Image left
    /// untouched; one whose result has already been computed (just not yet
    /// delivered on the UI thread) is too late to cancel and completes
    /// normally instead, the same way BackgroundWorker.CancelAsync treats
    /// work that already finished; no load at all is a silent no-op. Unlike
    /// a new LoadAsync, this does not disown the load - PostResult still
    /// recognises it as the current one, however its Task ends up.
    /// </summary>
    public void CancelAsync() => _loadCts?.Cancel();

    /// <summary>Reads a local file directly; fetches anything else (only http/https is meant to reach here) through HttpClient, never on the UI thread.</summary>
    private static Image FetchImage(string url, CancellationToken token)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var bytes = SharedHttpClient.Value.GetByteArrayAsync(uri, token).GetAwaiter().GetResult();
            using var stream = new MemoryStream(bytes);
            return Image.FromStream(stream);
        }

        var path = uri is { IsFile: true } ? uri.LocalPath : url;
        token.ThrowIfCancellationRequested();

        return Image.FromFile(path);
    }

    private void PostResult(Task<Image> task, CancellationTokenSource cts, string url)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // The task has ended, so nothing reads the token any more, whatever happens to the result.
            cts.Dispose();

            // Disposed controls raise no more events, the same reason Dispose cancels a running load in the first place.
            if (IsDisposed) return;

            // A later LoadAsync already disowned this load: its result, whatever it is, no longer matters.
            if (_loadCts != cts) return;

            _loadCts = null;

            if (task.IsCanceled)
            {
                OnLoadCompleted(new AsyncCompletedEventArgs(null, true, null));
                return;
            }

            if (task.IsFaulted)
            {
                var error = task.Exception?.GetBaseException();
                if (_errorImage is { } errorImage) { _image = errorImage; Invalidate(); }

                OnLoadCompleted(new AsyncCompletedEventArgs(error, false, null));
                return;
            }

            // The Image setter clears ImageLocation, as a direct assignment should; this image came from it.
            Image = task.Result;
            _imageLocation = url;
            OnLoadProgressChanged(new ProgressChangedEventArgs(100, null));
            OnLoadCompleted(new AsyncCompletedEventArgs(null, false, null));
        });
    }
}
