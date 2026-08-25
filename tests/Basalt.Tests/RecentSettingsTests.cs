using Basalt.Core.Settings;

namespace Basalt.Tests;

/// <summary>
/// The solutions and files opened lately.
///
/// There was nothing of the sort: reopening yesterday's work meant finding it
/// in the file picker every time.
/// </summary>
public sealed class RecentSettingsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-recent-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, "");

        return path;
    }

    [Fact]
    public void TheNewestComesFirst()
    {
        var recent = new RecentSettings();

        recent.RememberSolution("/a/First.slnx");
        recent.RememberSolution("/a/Second.slnx");

        Assert.EndsWith("Second.slnx", recent.Solutions[0], StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningSomethingAgainMovesItUpRatherThanRepeatingIt()
    {
        var recent = new RecentSettings();

        recent.RememberSolution("/a/First.slnx");
        recent.RememberSolution("/a/Second.slnx");
        recent.RememberSolution("/a/First.slnx");

        Assert.Equal(2, recent.Solutions.Count);
        Assert.EndsWith("First.slnx", recent.Solutions[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ItKeepsTenAndNoMore()
    {
        var recent = new RecentSettings();

        for (var i = 0; i < 15; i++) recent.RememberSolution($"/a/S{i}.slnx");

        Assert.Equal(RecentSettings.Limit, recent.Solutions.Count);

        // The oldest are the ones that went.
        Assert.EndsWith("S14.slnx", recent.Solutions[0], StringComparison.Ordinal);
        Assert.DoesNotContain(recent.Solutions, p => p.EndsWith("S0.slnx", StringComparison.Ordinal));
    }

    [Fact]
    public void SolutionsAndFilesAreKeptApart()
    {
        var recent = new RecentSettings();

        recent.RememberSolution("/a/S.slnx");
        recent.RememberFile("/a/F.vb");

        Assert.Single(recent.Solutions);
        Assert.Single(recent.Files);
    }

    [Fact]
    public void SomethingThatIsNoLongerThereIsNotOffered()
    {
        var recent = new RecentSettings();

        var here = Make("Here.slnx");

        recent.RememberSolution(here);
        recent.RememberSolution(Path.Combine(_root, "Gone.slnx"));

        var offered = recent.ExistingSolutions();

        Assert.Single(offered);
        Assert.Equal(here, offered[0]);
    }

    [Fact]
    public void PruningTakesOutWhatIsGone()
    {
        var recent = new RecentSettings();

        recent.RememberSolution(Make("Here.slnx"));
        recent.RememberSolution(Path.Combine(_root, "Gone.slnx"));

        Assert.True(recent.Prune());
        Assert.Single(recent.Solutions);

        // Nothing to do the second time.
        Assert.False(recent.Prune());
    }

    [Fact]
    public void AnEmptyPathIsIgnored()
    {
        var recent = new RecentSettings();

        recent.RememberSolution("");
        recent.RememberSolution("   ");

        Assert.Empty(recent.Solutions);
    }

    [Fact]
    public void ItSurvivesBeingSavedAndReadBack()
    {
        var store = new SettingsStore(Path.Combine(_root, "settings.json"));

        var settings = store.Load();

        settings.Recent.RememberSolution("/a/Kept.slnx");

        store.Save(settings);

        var reread = new SettingsStore(Path.Combine(_root, "settings.json")).Load();

        Assert.Single(reread.Recent.Solutions);
        Assert.EndsWith("Kept.slnx", reread.Recent.Solutions[0], StringComparison.Ordinal);
    }
}
