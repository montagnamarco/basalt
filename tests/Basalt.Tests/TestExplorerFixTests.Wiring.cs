namespace Basalt.Tests;

/// <summary>
/// The order in which the test window is connected to the build.
/// </summary>
/// <remarks>
/// The view model replays the last successful build the moment a subscription
/// is made, and the discovery that replay starts needs the test service. The
/// window's constructor cannot reach the replay today (no build precedes it),
/// so this guards the order in the source: red-proof by moving the
/// SubscribeToSuccessfulBuilds call above the service's assignment, which fails
/// TheBuildSubscriptionComesAfterTheTestServiceExists.
/// </remarks>
public class TestExplorerWiringFixTests
{
    [Fact]
    public void TheBuildSubscriptionComesAfterTheTestServiceExists()
    {
        var source = File.ReadAllText(FindUp("src/Basalt.Shell/MainWindow.axaml.cs"));

        var assigned = source.IndexOf("_testService = new DotnetTestService(", StringComparison.Ordinal);
        var subscribed = source.IndexOf("SubscribeToSuccessfulBuilds((project", StringComparison.Ordinal);

        Assert.True(assigned >= 0, "The test service is no longer assigned in MainWindow.");
        Assert.True(subscribed >= 0, "The test window no longer subscribes to builds.");
        Assert.True(subscribed > assigned,
            "The build subscription replays a past build at once, before the test service exists.");
    }

    private static string FindUp(string relative)
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null && !File.Exists(Path.Combine(dir, relative))) dir = Path.GetDirectoryName(dir);

        return dir is null ? relative : Path.Combine(dir, relative);
    }
}
