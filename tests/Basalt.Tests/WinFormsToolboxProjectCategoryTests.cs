using Basalt.Designer.Toolbox;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The toolbox group of a project's own controls: it carries the project's name, keeps each control's
/// own name, and follows every built-in group whatever the project is called.
/// </summary>
public class WinFormsToolboxProjectCategoryTests
{
    [Fact]
    public void AProjectControlKeepsItsNameAndTakesTheProjectGroup()
    {
        var item = Assert.Single(WinFormsToolboxCatalog.UnderCategory(
            [new WinFormsToolboxItem(typeof(ProjectControl))], "App Components"));

        Assert.Equal(typeof(ProjectControl), item.ControlType);
        Assert.Equal(nameof(ProjectControl), item.DisplayName);
        Assert.Equal("App Components", item.Category);
    }

    [Fact]
    public void TheProjectGroupFollowsEveryBuiltInGroupWhateverTheProjectIsCalled()
    {
        var builtIn = new WinFormsToolboxItem(typeof(Forms.Button));
        var project = WinFormsToolboxCatalog.UnderCategory(
            [new WinFormsToolboxItem(typeof(ProjectControl))], "Aardvark Components");

        var ordered = WinFormsToolboxCatalog.Order([.. project, builtIn]);

        Assert.Equal(WinFormsToolboxCatalog.Common, ordered[0].Category);
        Assert.Equal("Aardvark Components", ordered[1].Category);
    }

    [Fact]
    public void TwoProjectGroupsAreOrderedByNameAfterTheBuiltInOnes()
    {
        var zed = WinFormsToolboxCatalog.UnderCategory([new WinFormsToolboxItem(typeof(ProjectControl))], "Zed Components");
        var app = WinFormsToolboxCatalog.UnderCategory([new WinFormsToolboxItem(typeof(OtherProjectControl))], "App Components");

        var ordered = WinFormsToolboxCatalog.Order([.. zed, .. app, new WinFormsToolboxItem(typeof(Forms.Label))]);

        Assert.Equal(
            new[] { WinFormsToolboxCatalog.Common, "App Components", "Zed Components" },
            ordered.Select(item => item.Category).Distinct().ToArray());
    }

    public sealed class ProjectControl;

    public sealed class OtherProjectControl;
}
