using System.Drawing;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsPropertyEditorPopupPlacementTests
{
    [Fact]
    public void DropDownFormOpensAtTheGivenScreenPointInsteadOfTheOrigin()
    {
        // A drop-down form with FormStartPosition.Manual and no Location opens at (0,0), away
        // from the row that asked for it. The form is built but not shown, so this runs headless.
        using var control = new Forms.Button { Size = new Size(120, 40) };

        using var form = WinFormsDesignerView.CreateDropDownForm(control, new Point(320, 240));

        Assert.Equal(new Point(320, 240), form.Location);
        Assert.Equal(Forms.FormStartPosition.Manual, form.StartPosition);
        Assert.Equal(new Size(120, 40), form.ClientSize);
    }

    [Fact]
    public void DropDownPlacementKeepsThePointerWhenTheDropDownFits()
    {
        var area = new Rectangle(0, 25, 1920, 1055);

        var location = WinFormsDesignerView.DropDownLocation(new Point(500, 300), new Size(200, 120), area);

        Assert.Equal(new Point(500, 300), location);
    }

    [Fact]
    public void DropDownPlacementMovesBackOntoTheWorkingAreaNearItsRightAndBottomEdges()
    {
        // The working area starts below a menu bar, so the top-left clamp is not (0,0).
        var area = new Rectangle(0, 25, 1920, 1055);

        var location = WinFormsDesignerView.DropDownLocation(new Point(1900, 1070), new Size(200, 120), area);

        Assert.Equal(new Point(1720, 960), location);
    }

    [Fact]
    public void DropDownPlacementMovesBackOntoTheWorkingAreaFromOutsideItsTopLeftEdges()
    {
        var area = new Rectangle(0, 25, 1920, 1055);

        var location = WinFormsDesignerView.DropDownLocation(new Point(-40, -10), new Size(200, 120), area);

        Assert.Equal(new Point(0, 25), location);
    }

    [Fact]
    public void OversizedDropDownIsPinnedToTheWorkingAreaCorner()
    {
        var area = new Rectangle(0, 25, 800, 600);

        var location = WinFormsDesignerView.DropDownLocation(new Point(700, 500), new Size(1000, 900), area);

        Assert.Equal(new Point(0, 25), location);
    }
}
