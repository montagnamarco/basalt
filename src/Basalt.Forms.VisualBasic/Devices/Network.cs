using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.VisualBasic.FileIO;

namespace Microsoft.VisualBasic.Devices;

/// <summary>
/// Reachability, ping and file transfer, as My.Computer.Network exposes them
/// to a VB application.
/// </summary>
/// <remarks>
/// Download and upload go through HttpClient, called synchronously (.GetAwaiter().GetResult())
/// because every overload here is a blocking VB call, exactly as Microsoft's own
/// WebClient-based implementation blocks the caller. showUI and onUserCancel
/// are accepted for signature compatibility but have no effect: Basalt has no
/// progress dialog to show or cancel, so there is nothing for them to govern;
/// an I/O failure still throws, as it would with showUI set to false on Windows.
/// </remarks>
public class Network
{
    public bool IsAvailable => NetworkInterface.GetIsNetworkAvailable();

    public event NetworkAvailableEventHandler? NetworkAvailabilityChanged
    {
        // One subscription to the platform event however many handlers there are, so each handler hears a change once.
        add
        {
            var first = _handlers is null;
            _handlers += value;
            if (first && _handlers is not null)
            {
                NetworkChange.NetworkAvailabilityChanged += OnPlatformAvailabilityChanged;
                PlatformSubscriptions++;
            }
        }
        remove
        {
            _handlers -= value;
            if (_handlers is null && PlatformSubscriptions > 0)
            {
                NetworkChange.NetworkAvailabilityChanged -= OnPlatformAvailabilityChanged;
                PlatformSubscriptions--;
            }
        }
    }

    private NetworkAvailableEventHandler? _handlers;

    /// <summary>How many times this instance listens to the platform event: never more than once.</summary>
    internal int PlatformSubscriptions { get; private set; }

    private void OnPlatformAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        _handlers?.Invoke(this, new NetworkAvailableEventArgs(e.IsAvailable));

    public bool Ping(string hostNameOrAddress) => Ping(hostNameOrAddress, DefaultTimeout);

    public bool Ping(Uri address) => Ping(address.Host, DefaultTimeout);

    public bool Ping(string hostNameOrAddress, int timeout)
    {
        // Documented not to throw when the destination cannot be reached: an
        // unknown host, a firewall or no network all come back as "not reachable".
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send(hostNameOrAddress, timeout);

            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public bool Ping(Uri address, int timeout) => Ping(address.Host, timeout);

    public void DownloadFile(string address, string destinationFileName) =>
        DownloadFileCore(new Uri(address), destinationFileName, null, DefaultTimeout, overwrite: false);

    public void DownloadFile(Uri address, string destinationFileName) =>
        DownloadFileCore(address, destinationFileName, null, DefaultTimeout, overwrite: false);

    public void DownloadFile(string address, string destinationFileName, string userName, string password) =>
        DownloadFileCore(new Uri(address), destinationFileName, Credentials(userName, password), DefaultTimeout, overwrite: false);

    public void DownloadFile(Uri address, string destinationFileName, string userName, string password) =>
        DownloadFileCore(address, destinationFileName, Credentials(userName, password), DefaultTimeout, overwrite: false);

    public void DownloadFile(string address, string destinationFileName, string userName, string password, bool showUI, int connectionTimeout, bool overwrite) =>
        DownloadFileCore(new Uri(address), destinationFileName, Credentials(userName, password), connectionTimeout, overwrite);

    public void DownloadFile(string address, string destinationFileName, string userName, string password, bool showUI, int connectionTimeout, bool overwrite, UICancelOption onUserCancel) =>
        DownloadFileCore(new Uri(address), destinationFileName, Credentials(userName, password), connectionTimeout, overwrite);

    public void DownloadFile(Uri address, string destinationFileName, string userName, string password, bool showUI, int connectionTimeout, bool overwrite) =>
        DownloadFileCore(address, destinationFileName, Credentials(userName, password), connectionTimeout, overwrite);

    public void DownloadFile(Uri address, string destinationFileName, string userName, string password, bool showUI, int connectionTimeout, bool overwrite, UICancelOption onUserCancel) =>
        DownloadFileCore(address, destinationFileName, Credentials(userName, password), connectionTimeout, overwrite);

    public void DownloadFile(Uri address, string destinationFileName, ICredentials networkCredentials, bool showUI, int connectionTimeout, bool overwrite) =>
        DownloadFileCore(address, destinationFileName, networkCredentials, connectionTimeout, overwrite);

    public void DownloadFile(Uri address, string destinationFileName, ICredentials networkCredentials, bool showUI, int connectionTimeout, bool overwrite, UICancelOption onUserCancel) =>
        DownloadFileCore(address, destinationFileName, networkCredentials, connectionTimeout, overwrite);

    public void UploadFile(string sourceFileName, string address) =>
        UploadFileCore(sourceFileName, new Uri(address), null, DefaultTimeout);

    public void UploadFile(string sourceFileName, Uri address) =>
        UploadFileCore(sourceFileName, address, null, DefaultTimeout);

    public void UploadFile(string sourceFileName, string address, string userName, string password) =>
        UploadFileCore(sourceFileName, new Uri(address), Credentials(userName, password), DefaultTimeout);

    public void UploadFile(string sourceFileName, Uri address, string userName, string password) =>
        UploadFileCore(sourceFileName, address, Credentials(userName, password), DefaultTimeout);

    public void UploadFile(string sourceFileName, string address, string userName, string password, bool showUI, int connectionTimeout) =>
        UploadFileCore(sourceFileName, new Uri(address), Credentials(userName, password), connectionTimeout);

    public void UploadFile(string sourceFileName, string address, string userName, string password, bool showUI, int connectionTimeout, UICancelOption onUserCancel) =>
        UploadFileCore(sourceFileName, new Uri(address), Credentials(userName, password), connectionTimeout);

    public void UploadFile(string sourceFileName, Uri address, string userName, string password, bool showUI, int connectionTimeout) =>
        UploadFileCore(sourceFileName, address, Credentials(userName, password), connectionTimeout);

    public void UploadFile(string sourceFileName, Uri address, string userName, string password, bool showUI, int connectionTimeout, UICancelOption onUserCancel) =>
        UploadFileCore(sourceFileName, address, Credentials(userName, password), connectionTimeout);

    public void UploadFile(string sourceFileName, Uri address, ICredentials networkCredentials, bool showUI, int connectionTimeout) =>
        UploadFileCore(sourceFileName, address, networkCredentials, connectionTimeout);

    public void UploadFile(string sourceFileName, Uri address, ICredentials networkCredentials, bool showUI, int connectionTimeout, UICancelOption onUserCancel) =>
        UploadFileCore(sourceFileName, address, networkCredentials, connectionTimeout);

    // Matches WebClient's historical 100-second default when a caller does not say otherwise.
    private const int DefaultTimeout = 100_000;

    private static ICredentials Credentials(string userName, string password) => new NetworkCredential(userName, password);

    private static HttpClient CreateClient(ICredentials? credentials, int timeoutMilliseconds)
    {
        var handler = new HttpClientHandler();
        if (credentials is not null) handler.Credentials = credentials;

        return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds) };
    }

    private static void DownloadFileCore(Uri address, string destinationFileName, ICredentials? credentials, int connectionTimeout, bool overwrite)
    {
        if (!overwrite && File.Exists(destinationFileName))
            throw new IOException($"'{destinationFileName}' already exists.");

        using var client = CreateClient(credentials, connectionTimeout);
        using var bytes = client.GetStreamAsync(address).GetAwaiter().GetResult();
        using var destination = File.Create(destinationFileName);
        bytes.CopyTo(destination);
    }

    private static void UploadFileCore(string sourceFileName, Uri address, ICredentials? credentials, int connectionTimeout)
    {
        // As WebClient.UploadFile, which VB's UploadFile wraps: an HTTP POST of a multipart form with the file in a "file" field.
        using var client = CreateClient(credentials, connectionTimeout);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(File.OpenRead(sourceFileName));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", Path.GetFileName(sourceFileName));

        using var response = client.PostAsync(address, form).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }
}
