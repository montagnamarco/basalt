using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// What the changes panel says, beyond the list of files.
/// </summary>
public class GitChangesPanelLookTests
{
    private static IReadOnlyList<FileChange> Sample() =>
    [
        new("src/Basalt.Shell/Controls/ToolboxPanel.cs", FileChangeKind.Modified, true),
        new("src/Basalt.Shell/Controls/ToolboxIcons.cs", FileChangeKind.Added, true),
        new("tests/Basalt.Tests/GitPanelTests.cs", FileChangeKind.Modified, false),
        new("README.md", FileChangeKind.Modified, false),
        new("old/Legacy.vb", FileChangeKind.Deleted, false),
        new("notes.txt", FileChangeKind.Untracked, false),
    ];

    [AvaloniaFact]
    public void TheNameIsSeparateFromTheFolder()
    {
        // Shown as one string the name sits at the right, where a narrow
        // panel's ellipsis eats it first: twenty rows reading
        // "src/Basalt.Shell/Controls/Too…" say nothing at all.
        var row = new ChangeRow(
            new FileChange("src/Basalt.Shell/Controls/ToolboxPanel.cs",
                FileChangeKind.Modified, false));

        Assert.Equal("ToolboxPanel.cs", row.Name);
        Assert.Equal("src/Basalt.Shell/Controls", row.Folder);
    }

    [AvaloniaFact]
    public void AFileAtTheRootHasNoFolder()
    {
        Assert.Equal("", new ChangeRow(
            new FileChange("README.md", FileChangeKind.Modified, false)).Folder);
    }

    [AvaloniaFact]
    public void EachKindOfChangeIsDrawnInItsOwnColour()
    {
        // Green added, amber modified, red deleted: the conventions every
        // other client uses, because the list is scanned for the red one and
        // the letter is read only after the row is found.
        Color Of(FileChangeKind kind) =>
            new ChangeRow(new FileChange("a.vb", kind, false)).Colour;

        Assert.NotEqual(Of(FileChangeKind.Added), Of(FileChangeKind.Modified));
        Assert.NotEqual(Of(FileChangeKind.Modified), Of(FileChangeKind.Deleted));
        Assert.NotEqual(Of(FileChangeKind.Added), Of(FileChangeKind.Deleted));
    }

    [AvaloniaFact]
    public void CommitIsRefusedWithNothingStaged()
    {
        // git refuses it too, with an error in the output pane a moment
        // later. Saying so in the panel says it before the attempt.
        var panel = new GitChangesPanel();

        panel.Show("master", [
            new FileChange("a.vb", FileChangeKind.Modified, false)
        ], new UpstreamState("origin/master", 0, 0));

        panel.CommitMessage = "Un messaggio";

        Assert.False(panel.CanCommit);

        var asked = false;
        panel.CommitRequested += (_, _) => asked = true;

        panel.RequestCommitForTests();

        // And the keyboard path is refused too, not only the button: the
        // button being grey does not stop Ctrl+Enter.
        Assert.False(asked);
    }

    [AvaloniaFact]
    public void CommitIsRefusedWithNoMessage()
    {
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 0, 0));

        Assert.False(panel.CanCommit);

        panel.CommitMessage = "   ";

        Assert.False(panel.CanCommit);
    }

    [AvaloniaFact]
    public void CommitIsOfferedOnceThereIsBothAMessageAndSomethingStaged()
    {
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 0, 0));
        panel.CommitMessage = "Un messaggio";

        Assert.True(panel.CanCommit);
    }

    [AvaloniaFact]
    public void AmendingIsAllowedWithNothingStaged()
    {
        // Correcting the previous message is a commit with nothing staged,
        // and it is the reason amend exists: refusing it would make the tick
        // box useless in the case it was added for.
        var panel = new GitChangesPanel();

        panel.Show("master", [
            new FileChange("a.vb", FileChangeKind.Modified, false)
        ], new UpstreamState("origin/master", 0, 0));

        panel.CommitMessage = "Messaggio corretto";
        panel.IsAmending = true;

        Assert.True(panel.CanCommit);
    }

    [AvaloniaFact]
    public void TheHeaderSaysWhatThereIsToSendAndToReceive()
    {
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 3, 1));

        Assert.Contains("3", panel.UpstreamText);
        Assert.Contains("1", panel.UpstreamText);

        // Both buttons live, because there is something to do with each.
        Assert.True(panel.CanPush);
        Assert.True(panel.CanPull);
    }

    [AvaloniaFact]
    public void NothingToPushMeansPushIsNotOffered()
    {
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 0, 2));

        Assert.False(panel.CanPush);
        Assert.True(panel.CanPull);
    }

    [AvaloniaFact]
    public void AnUnpublishedBranchOffersPushAndNotPull()
    {
        // There is nothing to pull from a remote that has never heard of the
        // branch, and publishing it is a push.
        var panel = new GitChangesPanel();

        panel.Show("nuovo-ramo", Sample(), new UpstreamState(null, 0, 0));

        Assert.True(panel.CanPush);
        Assert.False(panel.CanPull);
        Assert.DoesNotContain("up to date", panel.UpstreamText);
    }

    [AvaloniaFact]
    public void WithNoUpstreamReadTheHeaderClaimsNothing()
    {
        // Called through the older overload, which knows nothing about the
        // remote: saying "up to date" there would claim the branch is in step
        // with something nobody has asked about.
        var panel = new GitChangesPanel();

        panel.Show("master", Sample());

        Assert.Equal("", panel.UpstreamText);
    }

    [AvaloniaFact]
    public void StageAllOffersEveryUnstagedFile()
    {
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 0, 0));

        var asked = false;
        panel.StageAllRequested += (_, _) => asked = true;

        panel.StageAllForTests();

        Assert.True(asked);
        Assert.Equal(4, panel.Unstaged.Count);
    }

    [AvaloniaFact]
    public void ThePanelDrawsWhatItSays()
    {
        // Rendered and looked at. Every assertion above passes on a panel
        // that throws on each draw, and the layout mistakes — a list given
        // half the panel to show two rows, a counter too faint to read — are
        // only visible in the picture.
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 3, 1));
        panel.CommitMessage = "Improve the git panel so it says what it is doing";

        var window = new Window { Content = panel, Width = 340, Height = 620 };

        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var target = new RenderTargetBitmap(new Avalonia.PixelSize(340, 620));

        target.Render(window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-git-changes.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 2000, "the render is empty");

        window.Close();
    }

    [AvaloniaFact]
    public void TheListsTakeOnlyTheRoomTheyNeed()
    {
        // Given half the panel each, two staged files leave a hand's width of
        // nothing between the lists and the unstaged list scrolls with room
        // to spare above it.
        var panel = new GitChangesPanel();

        panel.Show("master", Sample(), new UpstreamState("origin/master", 0, 0));

        var window = new Window { Content = panel, Width = 340, Height = 620 };

        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var lists = panel.GetVisualDescendants().OfType<ListBox>().ToList();

        Assert.Equal(2, lists.Count);

        // Two rows and four rows: the taller list is the one with more in it,
        // and neither is stretched to a share of the panel.
        var staged = lists[0].Bounds.Height;
        var unstaged = lists[1].Bounds.Height;

        Assert.True(staged > 0 && unstaged > 0, "a list has no height");
        Assert.True(unstaged > staged, $"staged {staged}, unstaged {unstaged}");

        window.Close();
    }
}
