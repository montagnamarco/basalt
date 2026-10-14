using Basalt.Core.Services;
using Basalt.Workspace;
using Basalt.Workspace.Testing;

using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>
/// Discoveries that go stale, run in the background, or wait for the build gate.
/// </summary>
/// <remarks>
/// Red-proof, one defect at a time, against this file: removing the older
/// discovery's cancel in LatestDiscovery.Begin fails
/// ANewerDiscoveryCancelsTheOlderOne; removing the generation bump in
/// LatestDiscovery.Cancel fails ClosingTheSolutionMakesTheDiscoveryInProgressStale;
/// removing the per-project cancel check in TestDiscovery fails
/// ACancelledDiscoveryStopsBeforeTheNextProject; running the scan on the
/// caller's thread fails TheFolderScanRunsOffTheCallingThread; entering the
/// bin and obj folders fails ScanningSkipsBuildOutputWithoutEnteringIt;
/// letting discovery bypass the build gate fails
/// DiscoveryWaitsForTheBuildGateAndHoldsItThroughTheListing; dropping the
/// metadata reader's InvalidOperationException from the catch fails
/// AnAssemblyWithoutManagedMetadataHasNoTests.
/// </remarks>
public class TestDiscoveryStaleFixTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-stale", Guid.NewGuid().ToString("N"));

    public TestDiscoveryStaleFixTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ANewerDiscoveryCancelsTheOlderOne()
    {
        var latest = new LatestDiscovery();

        var older = latest.Begin();
        var newer = latest.Begin();

        Assert.True(older.Token.IsCancellationRequested, "The older discovery kept running.");
        Assert.False(newer.Token.IsCancellationRequested);
        Assert.False(latest.IsCurrent(older));
        Assert.True(latest.IsCurrent(newer));
    }

    [Fact]
    public void ClosingTheSolutionMakesTheDiscoveryInProgressStale()
    {
        // Its result belongs to the solution that closed, so it must not be shown.
        var latest = new LatestDiscovery();
        var run = latest.Begin();

        latest.Cancel();

        Assert.True(run.Token.IsCancellationRequested);
        Assert.False(latest.IsCurrent(run));
    }

    [Fact]
    public async Task ACancelledDiscoveryStopsBeforeTheNextProject()
    {
        WriteTestProject(Path.Combine(_root, "First"), "First.vbproj");
        WriteTestProject(Path.Combine(_root, "Second"), "Second.vbproj");

        using var cancel = new CancellationTokenSource();
        var service = new RecordingService(() => cancel.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TestDiscovery.DiscoverFolderAsync(_root, service, _ => { }, cancel.Token));

        // One project was asked; the cancel stopped the other from being started.
        Assert.Single(service.Asked);
    }

    [Fact]
    public async Task TheFolderScanRunsOffTheCallingThread()
    {
        // The scan reads every project file, so it must not run on the interface
        // thread. The scan here waits for a release; if the call ran the scan
        // itself, the call would not return until the wait gave up.
        using var release = new ManualResetEventSlim(false);

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var discovery = TestDiscovery.DiscoverFolderAsync(
            _root,
            new RecordingService(() => { }),
            _ => { },
            default,
            folder =>
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return [];
            });

        var callReturned = clock.Elapsed;
        release.Set();

        await discovery;

        Assert.True(callReturned < TimeSpan.FromSeconds(5),
            $"The call blocked for {callReturned.TotalSeconds:F1} s: the scan ran on the calling thread.");
    }

    [Fact]
    public void ScanningSkipsBuildOutputWithoutEnteringIt()
    {
        WriteTestProject(Path.Combine(_root, "Tests"), "Tests.vbproj");
        WriteTestProject(Path.Combine(_root, "bin", "Debug"), "Copy.vbproj");
        WriteTestProject(Path.Combine(_root, "obj"), "Generated.vbproj");

        var entered = new List<string>();

        var found = TestProjectDetector.ScanForTestProjects(_root, entered.Add);

        Assert.Single(found);
        Assert.DoesNotContain(entered, folder => Path.GetFileName(folder) is "bin" or "obj" or "Debug");
    }

    [Fact]
    public async Task DiscoveryWaitsForTheBuildGateAndHoldsItThroughTheListing()
    {
        // A build holds the gate; a discovery must not build, list or read the
        // assembly under it. The project is not a real one: the dotnet run fails
        // fast, and only the waiting is under test.
        var project = Path.Combine(_root, "Bogus", "Bogus.vbproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");

        var gate = new BuildGate();
        var service = new DotnetTestService(gate);

        var held = await gate.AcquireAsync("test", _ => { }, CancellationToken.None);

        var discovery = service.DiscoverAsync(project);

        await Task.Delay(3000);
        Assert.False(discovery.IsCompleted, "Discovery ran while the build gate was held.");

        held.Dispose();

        // The project does not build, so the discovery ends in its failure: that is
        // the answer the waiting led to, and it must reach the caller.
        await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.WaitAsync(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task AFailedProjectIsReportedAndTheOthersAreStillFound()
    {
        WriteTestProject(Path.Combine(_root, "Good"), "Good.vbproj");
        WriteTestProject(Path.Combine(_root, "Broken"), "Broken.vbproj");

        var reported = new List<string>();

        var result = await TestDiscovery.DiscoverFolderAsync(_root, new FailingService(), reported.Add);

        // The failure is known to the caller, so the window does not replace its tree.
        Assert.False(result.Succeeded);
        Assert.Single(result.Failures);
        Assert.Single(reported);

        var found = Assert.Single(result.Tests);
        Assert.Equal("Good.Test", found.FullyQualifiedName);
    }

    [Fact]
    public void AnAssemblyWithoutManagedMetadataHasNoTests()
    {
        // A valid PE image with no CLI header: a native library, say. The
        // metadata reader refuses it with InvalidOperationException, which must
        // not fail the whole discovery.
        var path = Path.Combine(_root, "native.dll");
        File.WriteAllBytes(path, PeWithoutMetadata());

        Assert.Empty(TestNameResolver.TestMethodsByName(path));
    }

    /// <summary>A PE32+ image with headers and no data directories.</summary>
    private static byte[] PeWithoutMetadata()
    {
        var image = new byte[0x40 + 4 + 20 + 240];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(image, 0x3C);

        image[0x40] = (byte)'P';
        image[0x41] = (byte)'E';

        BitConverter.GetBytes((ushort)0x8664).CopyTo(image, 0x44);
        BitConverter.GetBytes((ushort)240).CopyTo(image, 0x54);
        BitConverter.GetBytes((ushort)0x0022).CopyTo(image, 0x56);

        BitConverter.GetBytes((ushort)0x20B).CopyTo(image, 0x58);
        BitConverter.GetBytes(16u).CopyTo(image, 0x58 + 108);

        return image;
    }

    private static void WriteTestProject(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, fileName),
            "<Project Sdk=\"MSTest.Sdk/3.9.3\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A service that fails for the project named Broken and finds one test in the others.</summary>
    private sealed class FailingService : ITestService
    {
        public Task<IReadOnlyList<TestCase>> DiscoverAsync(string projectPath, CancellationToken ct = default)
        {
            if (projectPath.Contains("Broken", StringComparison.Ordinal))
                return Task.FromException<IReadOnlyList<TestCase>>(new InvalidOperationException("The build failed."));

            return Task.FromResult<IReadOnlyList<TestCase>>([new TestCase("Good.Test", projectPath)]);
        }

        public Task<IReadOnlyList<TestResult>> RunAsync(
            string projectPath, string? filter = null, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<TestResult>>(new NotSupportedException());

        public event EventHandler<string>? OutputReceived
        {
            add { }
            remove { }
        }
    }

    /// <summary>A service that records the projects it is asked about and runs a hook on each.</summary>
    private sealed class RecordingService(Action onDiscover) : ITestService
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<TestCase>> DiscoverAsync(string projectPath, CancellationToken ct = default)
        {
            Asked.Add(projectPath);
            onDiscover();

            return Task.FromResult<IReadOnlyList<TestCase>>([]);
        }

        public Task<IReadOnlyList<TestResult>> RunAsync(
            string projectPath, string? filter = null, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<TestResult>>(new NotSupportedException());

        public event EventHandler<string>? OutputReceived
        {
            add { }
            remove { }
        }
    }
}
