using Basalt.Designer;
using Box = Basalt.Designer.AlignmentGuides.Box;

namespace Basalt.Tests;

/// <summary>
/// Snapping a dragged control to the ones around it.
/// </summary>
public sealed class AlignmentGuidesTests
{
    [Fact]
    public void AnEdgeNearAnotherSnapsToIt()
    {
        // Two pixels off is invisible while dragging and plainly wrong once
        // the pointer is up.
        var dragged = new Box(48, 10, 50, 20);
        var other = new Box(50, 60, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other]);

        Assert.Equal(50, snap.X);
        Assert.Single(snap.Guides);
    }

    [Fact]
    public void AnEdgeFarAwayDoesNotSnap()
    {
        var dragged = new Box(20, 10, 50, 20);
        var other = new Box(200, 60, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other]);

        Assert.Equal(20, snap.X);
        Assert.Empty(snap.Guides);
    }

    [Fact]
    public void CentresLineUpToo()
    {
        // Centring one control under another is the commonest alignment there
        // is, and edges alone never catch it.
        var dragged = new Box(38, 10, 20, 20);
        var other = new Box(20, 60, 60, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other]);

        // The other's centre is 50, so a 20-wide box centres at x = 40.
        Assert.Equal(40, snap.X);
    }

    [Fact]
    public void TheRightEdgeSnapsAsWellAsTheLeft()
    {
        // Whichever of the dragged box's own sides is nearest wins, which is
        // what makes the gesture feel like help rather than a fight.
        var dragged = new Box(48, 10, 50, 20);
        var other = new Box(20, 60, 80, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other]);

        // Its right edge at 98 is two from the other's at 100.
        Assert.Equal(50, snap.X);
    }

    [Fact]
    public void BothAxesSnapAtOnce()
    {
        var dragged = new Box(48, 58, 50, 20);
        var other = new Box(50, 60, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other]);

        Assert.Equal(50, snap.X);
        Assert.Equal(60, snap.Y);
        Assert.Equal(2, snap.Guides.Count);
    }

    [Fact]
    public void HoldingAltPlacesItExactly()
    {
        // Snapping that cannot be turned off is snapping that stops you
        // putting something where you meant to.
        var dragged = new Box(48, 10, 50, 20);
        var other = new Box(50, 60, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [other], enabled: false);

        Assert.Equal(48, snap.X);
        Assert.Empty(snap.Guides);
    }

    [Fact]
    public void WithNothingToAlignToItStaysPut()
    {
        var dragged = new Box(48, 10, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, []);

        Assert.Equal(48, snap.X);
        Assert.Equal(10, snap.Y);
    }

    [Fact]
    public void TheNearestCandidateWins()
    {
        // Two within range: the closer one is the one meant.
        var dragged = new Box(48, 10, 50, 20);

        var snap = AlignmentGuides.SnapTo(dragged, [new Box(45, 60, 10, 10), new Box(49, 80, 10, 10)]);

        Assert.Equal(49, snap.X);
    }
}
