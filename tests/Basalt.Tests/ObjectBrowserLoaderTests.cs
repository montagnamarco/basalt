using Basalt.Workspace.ObjectBrowser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// The Object Browser never blocks the interface thread and never applies an
/// answer to a question that has since been replaced.
/// </summary>
public sealed class ObjectBrowserLoaderTests
{
    private static Compilation Compile() => VisualBasicCompilation.Create(
        "Probe",
        [VisualBasicSyntaxTree.ParseText("Public Class Probe\nEnd Class\n")],
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
        new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public async Task ASlowAnswerThatWasSupersededIsDroppedAndTheNewestWins()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // The first question waits for permission and ignores its token, as
        // work that is already past its last cancellation check does; only
        // the loader can keep its answer from being used.
        var service = new ObjectBrowserService(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.SetResult();
                await release.Task;
            }

            return [new BrowserProject(calls == 1 ? "Old" : "New", Compile())];
        });

        using var loader = new ObjectBrowserLoader(service);

        var first = loader.LatestAsync("libraries", (s, ct) => s.GetLibrariesAsync(ct));
        await firstStarted.Task;

        var second = loader.LatestAsync("libraries", (s, ct) => s.GetLibrariesAsync(ct));
        var newest = await second;

        release.SetResult();
        var stale = await first;

        Assert.Null(stale);
        Assert.NotNull(newest);
        Assert.Contains(newest, library => library.Name == "New");
    }

    [Fact]
    public async Task AQuestionIsCancelledThroughItsTokenWhenAnotherReplacesIt()
    {
        var sawCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var loader = new ObjectBrowserLoader(new ObjectBrowserService(_ => Task.FromResult<IReadOnlyList<BrowserProject>>([])));

        var first = loader.LatestAsync<string>("members", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                sawCancellation.SetResult(true);
                throw;
            }

            return "never";
        });

        await started.Task;

        var second = await loader.LatestAsync("members", (_, _) => Task.FromResult("answer"));

        Assert.Equal("answer", second);
        Assert.True(await sawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(await first);
    }

    [Fact]
    public async Task ChannelsAreIndependentButCancelAllDropsEverything()
    {
        using var loader = new ObjectBrowserLoader(new ObjectBrowserService(_ => Task.FromResult<IReadOnlyList<BrowserProject>>([])));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var types = loader.LatestAsync("types", async (_, _) =>
        {
            await release.Task;
            return "types";
        });

        // A different channel does not replace the first.
        Assert.Equal("members", await loader.LatestAsync("members", (_, _) => Task.FromResult("members")));

        loader.CancelAll();
        release.SetResult();

        Assert.Null(await types);
    }

    [Fact]
    public async Task TheCompilerIsAskedOffTheCallersThread()
    {
        var callerThread = -1;
        var workThread = -2;

        var service = new ObjectBrowserService(_ =>
        {
            workThread = Environment.CurrentManagedThreadId;
            return Task.FromResult<IReadOnlyList<BrowserProject>>([new BrowserProject("Probe", Compile())]);
        });

        // The stand-in for the interface thread: a thread of its own that
        // waits for the answer, so a question answered inline would come back
        // having run on it.
        Task<IReadOnlyList<BrowserLibrary>>? pending = null;
        var interfaceThread = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            pending = service.GetLibrariesAsync();
            pending.Wait();
        });

        interfaceThread.Start();
        await Task.Run(interfaceThread.Join);

        Assert.NotEqual(callerThread, workThread);
        Assert.True(pending!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ChoosingALibraryDoesNotCancelTheListOfLibrariesBeingReloaded()
    {
        using var loader = new ObjectBrowserLoader(new ObjectBrowserService(_ => Task.FromResult<IReadOnlyList<BrowserProject>>([])));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var libraries = loader.LatestAsync("libraries", async (_, _) =>
        {
            await release.Task;
            return "libraries";
        });
        var members = loader.LatestAsync("members", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "members";
        });

        loader.CancelAllExcept("libraries");
        release.SetResult();

        Assert.Equal("libraries", await libraries);
        Assert.Null(await members);
    }

    [Fact]
    public async Task ARefreshDoesNotCancelANavigationInFlight()
    {
        using var loader = new ObjectBrowserLoader(new ObjectBrowserService(_ => Task.FromResult<IReadOnlyList<BrowserProject>>([])));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var navigate = loader.LatestAsync("navigate", async (_, _) =>
        {
            await release.Task;
            return "target";
        });

        loader.CancelAllExcept("navigate");
        release.SetResult();

        Assert.Equal("target", await navigate);
    }
}
