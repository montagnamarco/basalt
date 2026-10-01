using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.MyServices;

namespace Basalt.Forms.Tests.VisualBasic;

/// <summary>
/// Microsoft.VisualBasic.Devices (My.Computer) and Microsoft.VisualBasic.MyServices:
/// Clock, Keyboard, Mouse, ComputerInfo, Audio, Network, ServerComputer, Computer,
/// and the FileSystem/Clipboard/SpecialDirectories/Registry proxies.
/// </summary>
public class DevicesTests
{
    [Fact]
    public void Clock_reports_times_close_to_the_system_clock()
    {
        var clock = new Clock();
        var beforeLocal = DateTime.Now;
        var beforeTicks = Environment.TickCount;

        Assert.True(Math.Abs((clock.LocalTime - beforeLocal).TotalSeconds) < 5);
        Assert.True(Math.Abs((clock.GmtTime - DateTime.UtcNow).TotalSeconds) < 5);
        Assert.True(Math.Abs(clock.TickCount - beforeTicks) < 5000);
    }

    [Fact]
    public void Keyboard_SendKeys_is_not_supported()
    {
        var keyboard = new Keyboard();

        Assert.Throws<PlatformNotSupportedException>(() => keyboard.SendKeys("abc"));
        Assert.Throws<PlatformNotSupportedException>(() => keyboard.SendKeys("abc", true));
    }

    [Fact]
    public void Keyboard_lock_states_default_to_not_locked()
    {
        // Basalt.Forms has no Control.IsKeyLocked to ask, so these report the
        // documented default of "not locked" rather than guess at the real state.
        var keyboard = new Keyboard();

        Assert.False(keyboard.CapsLock);
        Assert.False(keyboard.NumLock);
        Assert.False(keyboard.ScrollLock);
    }

    [AvaloniaFact]
    public void Keyboard_reports_the_modifier_keys_actually_held_down()
    {
        var form = new Form { ClientSize = new Size(100, 100) };
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var keyboard = new Keyboard();

        form.Host!.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Shift);
        Assert.True(keyboard.ShiftKeyDown);
        Assert.False(keyboard.AltKeyDown);
        Assert.False(keyboard.CtrlKeyDown);
        form.Host!.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Shift);

        form.Host!.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.True(keyboard.CtrlKeyDown);
        Assert.False(keyboard.ShiftKeyDown);
        form.Host!.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);

        form.Close();
    }

    [Fact]
    public void Mouse_reports_a_three_button_wheel_mouse_not_swapped()
    {
        var mouse = new Mouse();

        Assert.False(mouse.ButtonsSwapped);
        Assert.True(mouse.WheelExists);
        Assert.Equal(SystemInformation.MouseWheelScrollLines, mouse.WheelScrollLines);
    }

    [Fact]
    public void ComputerInfo_reports_consistent_memory_and_os_values()
    {
        var info = new ComputerInfo();

        Assert.True(info.TotalPhysicalMemory > 0);
        Assert.True(info.AvailablePhysicalMemory <= info.TotalPhysicalMemory);
        Assert.Equal(info.TotalPhysicalMemory, info.TotalVirtualMemory);
        Assert.Equal(info.AvailablePhysicalMemory, info.AvailableVirtualMemory);
        Assert.False(string.IsNullOrEmpty(info.OSFullName));
        Assert.False(string.IsNullOrEmpty(info.OSPlatform));
        Assert.False(string.IsNullOrEmpty(info.OSVersion));
        Assert.NotNull(info.InstalledUICulture);
    }

    [Fact]
    public void Audio_Play_has_no_device_API_so_every_overload_throws()
    {
        var audio = new Audio();

        Assert.Throws<PlatformNotSupportedException>(() => audio.Play("sound.wav"));
        Assert.Throws<PlatformNotSupportedException>(() => audio.Play("sound.wav", AudioPlayMode.Background));
        Assert.Throws<PlatformNotSupportedException>(() => audio.Play(new byte[] { 1, 2, 3 }, AudioPlayMode.Background));
        Assert.Throws<PlatformNotSupportedException>(() => audio.Play(new MemoryStream(), AudioPlayMode.Background));
    }

    [Fact]
    public void Audio_Stop_does_nothing_because_nothing_is_ever_playing()
    {
        var audio = new Audio();

        // Must not throw: Stop is valid even though Play never succeeds.
        audio.Stop();
    }

    [Fact]
    public void Network_IsAvailable_matches_the_platform_answer()
    {
        var network = new Network();

        Assert.Equal(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(), network.IsAvailable);
    }

    [Fact]
    public void Network_Ping_succeeds_against_the_loopback_address()
    {
        var network = new Network();

        Assert.True(network.Ping("127.0.0.1"));
        Assert.True(network.Ping(new Uri("http://127.0.0.1/")));
    }

    [Fact]
    public void Network_Ping_returns_false_instead_of_throwing_for_an_unreachable_host()
    {
        var network = new Network();

        Assert.False(network.Ping("this.host.does.not.exist.invalid", 500));
    }

    [Fact]
    public void Network_DownloadFile_writes_the_response_body_to_disk()
    {
        using var server = new LoopbackServer();
        server.RespondWith("downloaded-content");
        var destination = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            new Network().DownloadFile(server.Address, destination);

            Assert.Equal("downloaded-content", File.ReadAllText(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [Fact]
    public void Network_DownloadFile_refuses_to_overwrite_unless_asked()
    {
        using var server = new LoopbackServer();
        server.RespondWith("new-content");
        var destination = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(destination, "existing-content");

        try
        {
            Assert.Throws<IOException>(() => new Network().DownloadFile(server.Address, destination));
            Assert.Equal("existing-content", File.ReadAllText(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [Fact]
    public void Network_UploadFile_sends_the_file_s_bytes_to_the_server()
    {
        using var server = new LoopbackServer();
        var source = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(source, "uploaded-content");

        try
        {
            new Network().UploadFile(source, server.Address);

            // A multipart POST, as WebClient.UploadFile sends it.
            Assert.Equal("POST", server.LastRequestMethod);
            Assert.StartsWith("multipart/form-data", server.LastRequestContentType);
            Assert.Contains("name=file", server.LastRequestBody);
            Assert.Contains("uploaded-content", server.LastRequestBody);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void NetworkAvailableEventArgs_carries_the_reachability_it_was_built_with()
    {
        var available = new NetworkAvailableEventArgs(true);
        var unavailable = new NetworkAvailableEventArgs(false);

        Assert.True(available.IsNetworkAvailable);
        Assert.False(unavailable.IsNetworkAvailable);
    }

    [Fact]
    public void Network_NetworkAvailabilityChanged_can_be_subscribed_and_unsubscribed()
    {
        var network = new Network();
        void Handler(object sender, NetworkAvailableEventArgs e) { }

        // Must not throw: hooking into System.Net.NetworkInformation.NetworkChange and back out.
        network.NetworkAvailabilityChanged += Handler;
        network.NetworkAvailabilityChanged -= Handler;
    }

    [Fact]
    public void ServerComputer_exposes_its_name_clock_file_system_info_and_network()
    {
        var computer = new ServerComputer();

        Assert.Equal(Environment.MachineName, computer.Name);
        Assert.NotNull(computer.Clock);
        Assert.NotNull(computer.FileSystem);
        Assert.NotNull(computer.Info);
        Assert.NotNull(computer.Network);
    }

    [Fact]
    public void ServerComputer_Registry_is_not_supported_off_Windows()
    {
        var computer = new ServerComputer();

        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(computer.Registry.CurrentUser);
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => computer.Registry.CurrentUser);
        }
    }

    [AvaloniaFact]
    public void Computer_adds_audio_clipboard_keyboard_mouse_and_screen_to_a_ServerComputer()
    {
        var computer = new Computer();

        Assert.NotNull(computer.Audio);
        Assert.NotNull(computer.Clipboard);
        Assert.NotNull(computer.Keyboard);
        Assert.NotNull(computer.Mouse);
        Assert.NotNull(computer.Screen);

        // Computer carries everything ServerComputer already has.
        Assert.Equal(Environment.MachineName, computer.Name);
    }

    [AvaloniaFact]
    public void ClipboardProxy_round_trips_text_through_the_system_clipboard()
    {
        var clipboard = CreateProxy<ClipboardProxy>();

        clipboard.SetText("hello from My.Computer.Clipboard");

        Assert.True(clipboard.ContainsText());
        Assert.Equal("hello from My.Computer.Clipboard", clipboard.GetText());

        clipboard.Clear();
        Assert.False(clipboard.ContainsText());
    }

    [Fact]
    public void FileSystemProxy_combines_paths_the_way_Path_Combine_would()
    {
        var fileSystem = CreateProxy<FileSystemProxy>();

        Assert.Equal(
            Microsoft.VisualBasic.FileIO.FileSystem.CombinePath("a", "b"),
            fileSystem.CombinePath("a", "b"));
    }

    [Fact]
    public void FileSystemProxy_writes_reads_and_deletes_a_file()
    {
        var fileSystem = CreateProxy<FileSystemProxy>();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        fileSystem.WriteAllText(path, "proxy wrote this", append: false);

        Assert.True(fileSystem.FileExists(path));
        Assert.Equal("proxy wrote this", fileSystem.ReadAllText(path));

        fileSystem.DeleteFile(path);
        Assert.False(fileSystem.FileExists(path));
    }

    [Fact]
    public void FileSystemProxy_creates_and_deletes_a_directory()
    {
        var fileSystem = CreateProxy<FileSystemProxy>();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        fileSystem.CreateDirectory(directory);
        Assert.True(fileSystem.DirectoryExists(directory));

        fileSystem.DeleteDirectory(directory, Microsoft.VisualBasic.FileIO.DeleteDirectoryOption.ThrowIfDirectoryNonEmpty);
        Assert.False(fileSystem.DirectoryExists(directory));
    }

    [Fact]
    public void FileSystemProxy_exposes_drives_and_special_directories()
    {
        var fileSystem = CreateProxy<FileSystemProxy>();

        Assert.NotEmpty(fileSystem.Drives);
        Assert.Equal(Microsoft.VisualBasic.FileIO.SpecialDirectories.Temp, fileSystem.SpecialDirectories.Temp);
    }

    [Fact]
    public void SpecialDirectoriesProxy_forwards_every_member_to_the_framework_s_static_type()
    {
        var proxy = CreateProxy<SpecialDirectoriesProxy>();

        // The framework throws for a folder the machine does not have (no Documents on a build agent): the proxy must do the same.
        AssertSame(() => Microsoft.VisualBasic.FileIO.SpecialDirectories.Desktop, () => proxy.Desktop);
        AssertSame(() => Microsoft.VisualBasic.FileIO.SpecialDirectories.MyDocuments, () => proxy.MyDocuments);
        AssertSame(() => Microsoft.VisualBasic.FileIO.SpecialDirectories.Temp, () => proxy.Temp);
    }

    private static void AssertSame(Func<string> framework, Func<string> proxy)
    {
        string expected;

        try
        {
            expected = framework();
        }
        catch (DirectoryNotFoundException)
        {
            Assert.Throws<DirectoryNotFoundException>(proxy);
            return;
        }

        Assert.Equal(expected, proxy());
    }

    [Fact]
    public void RegistryProxy_is_not_supported_off_Windows()
    {
        var registry = CreateProxy<RegistryProxy>();

        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(registry.CurrentUser);
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => registry.CurrentUser);
            Assert.Throws<PlatformNotSupportedException>(() => registry.GetValue("key", "value", "default"));
        }
    }

    /// <summary>A minimal HTTP server on loopback, so Network's download and upload are tested without real internet access.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private string _responseBody = string.Empty;

        public LoopbackServer()
        {
            var port = FreeTcpPort();
            Address = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(Address.ToString());
            _listener.Start();
            Listen();
        }

        public Uri Address { get; }

        public string LastRequestBody { get; private set; } = string.Empty;

        public string LastRequestMethod { get; private set; } = string.Empty;

        public string LastRequestContentType { get; private set; } = string.Empty;

        public void RespondWith(string body) => _responseBody = body;

        private void Listen() => Task.Run(async () =>
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();

                    LastRequestMethod = context.Request.HttpMethod;
                    LastRequestContentType = context.Request.ContentType ?? string.Empty;

                    if (context.Request.HttpMethod is "PUT" or "POST")
                    {
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        LastRequestBody = await reader.ReadToEndAsync();
                    }

                    var bytes = Encoding.UTF8.GetBytes(_responseBody);
                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    context.Response.OutputStream.Close();
                }
            }
            catch (HttpListenerException)
            {
                // The listener was stopped while a request was pending: the test is over.
            }
            catch (ObjectDisposedException)
            {
            }
        });

        private static int FreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }

        public void Dispose() => _listener.Close();
    }

    /// <summary>The proxies have no public constructor, as in Microsoft's assembly; My.Computer creates them.</summary>
    private static T CreateProxy<T>() => (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;

    [Fact]
    public void Two_handlers_share_one_subscription_to_the_platform_and_remove_cleanly()
    {
        var network = new Network();
        NetworkAvailableEventHandler first = (_, _) => { };
        NetworkAvailableEventHandler second = (_, _) => { };

        network.NetworkAvailabilityChanged += first;
        network.NetworkAvailabilityChanged += second;
        Assert.Equal(1, PlatformSubscriptions(network));

        network.NetworkAvailabilityChanged -= first;
        Assert.Equal(1, PlatformSubscriptions(network));

        network.NetworkAvailabilityChanged -= second;
        Assert.Equal(0, PlatformSubscriptions(network));
    }

    private static int PlatformSubscriptions(Network network) => network.PlatformSubscriptions;
}
