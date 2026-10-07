using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Basalt.Tests;

/// <summary>
/// Every check box in the app shares one style (Styles.axaml), compacted to
/// a Height of 20 so a column of switches reads as a group. Fluent's own
/// template keeps the box's cell a fixed 32 tall and top-aligns it, so once
/// squeezed into 20 the cell overflowed downward from the shared top edge
/// and the box ended up visibly lower than its label — reproduced here with
/// a plain check box, not the dialog that first exposed it
/// (NewSolutionDialogTreeTests.PlaceInSameDirectoryBoxIsVerticallyCentredOnItsLabel),
/// so the fix is proved at the level it was actually made: the global style.
/// </summary>
public sealed class CheckBoxAlignmentTests
{
    [AvaloniaFact]
    public void ABoxIsVerticallyCentredOnItsLabel()
    {
        var check = new CheckBox { Content = "Remember my choice" };
        var window = new Window { Content = check, Width = 200, Height = 60 };
        window.Show();
        for (var i = 0; i < 5; i++) window.UpdateLayout();

        var box = check.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
        var text = check.GetVisualDescendants().OfType<TextBlock>().Single();

        var boxCenter = Raster.BoundsIn(box, window).Y + Raster.BoundsIn(box, window).Height / 2.0;
        var textCenter = Raster.BoundsIn(text, window).Y + Raster.BoundsIn(text, window).Height / 2.0;

        window.Close();

        Assert.True(Math.Abs(boxCenter - textCenter) <= 2,
            $"the box centres at {boxCenter}, the label at {textCenter}");
    }
}
