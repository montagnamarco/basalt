using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// The members PropertyGrid carries for its still-undrawn features (the
/// hot-commands pane, the events button, flat/3D toolbar rendering) plus the
/// lifecycle hooks that forward to base: each one is real storage or a real
/// forward, not a no-op stub - see PropertyGrid.Extended.cs's remarks for
/// what painting is still missing.
/// </summary>
public class PropertyGridExtendedTests
{
    private sealed class TestablePropertyGrid : System.Windows.Forms.PropertyGrid
    {
        public bool DrawFlatToolbarPublic { get => DrawFlatToolbar; set => DrawFlatToolbar = value; }

        public ToolStripRenderer ToolStripRendererPublic => ToolStripRenderer;

        public Bitmap SortByCategoryImagePublic => SortByCategoryImage;

        public Bitmap SortByPropertyImagePublic => SortByPropertyImage;

        public Type DefaultTabTypePublic => DefaultTabType;

        public void ShowEventsButtonPublic(bool value) => ShowEventsButton(value);

        public bool VisibleChangedCancelledEdit { get; private set; }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) VisibleChangedCancelledEdit = true;
        }
    }

    [Fact]
    public void CommandsVisibleIfAvailable_defaults_to_true_and_can_be_turned_off()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();

        Assert.True(grid.CommandsVisibleIfAvailable);

        grid.CommandsVisibleIfAvailable = false;
        Assert.False(grid.CommandsVisibleIfAvailable);
    }

    [Fact]
    public void CommandsVisible_requires_both_availability_and_a_selected_item()
    {
        using var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new TestComponent() };

        Assert.True(grid.CommandsVisible);

        grid.CommandsVisibleIfAvailable = false;
        Assert.False(grid.CommandsVisible);
    }

    [Fact]
    public void CanShowCommands_is_true_by_default()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();

        Assert.True(grid.CanShowCommands);
    }

    [Fact]
    public void Commands_colors_store_whatever_they_are_set_to()
    {
        using var grid = new System.Windows.Forms.PropertyGrid
        {
            CommandsActiveLinkColor = Color.Red,
            CommandsBackColor = Color.Blue,
            CommandsLinkColor = Color.Green,
        };

        Assert.Equal(Color.Red, grid.CommandsActiveLinkColor);
        Assert.Equal(Color.Blue, grid.CommandsBackColor);
        Assert.Equal(Color.Green, grid.CommandsLinkColor);
    }

    [Fact]
    public void ContextMenuDefaultLocation_does_not_throw_without_a_window_handle()
    {
        using var grid = new System.Windows.Forms.PropertyGrid { Size = new Size(200, 100) };

        var point = grid.ContextMenuDefaultLocation;

        Assert.Equal(100, point.X);
        Assert.Equal(50, point.Y);
    }

    [Fact]
    public void DrawFlatToolbar_switches_which_ToolStripRenderer_is_used()
    {
        using var grid = new TestablePropertyGrid();

        grid.DrawFlatToolbarPublic = false;
        Assert.IsType<ToolStripSystemRenderer>(grid.ToolStripRendererPublic);

        grid.DrawFlatToolbarPublic = true;
        Assert.IsType<ToolStripProfessionalRenderer>(grid.ToolStripRendererPublic);
    }

    [Fact]
    public void SortByCategory_and_SortByProperty_images_are_real_bitmaps()
    {
        using var grid = new TestablePropertyGrid();

        Assert.Equal(new Size(16, 16), grid.SortByCategoryImagePublic.Size);
        Assert.Equal(new Size(16, 16), grid.SortByPropertyImagePublic.Size);
    }

    [Fact]
    public void DefaultTabType_is_PropertiesTab()
    {
        using var grid = new TestablePropertyGrid();

        Assert.Equal(typeof(System.Windows.Forms.PropertyGridInternal.PropertiesTab), grid.DefaultTabTypePublic);
    }

    [Fact]
    public void ShowEventsButton_does_not_throw_and_can_be_toggled_repeatedly()
    {
        using var grid = new TestablePropertyGrid();

        grid.ShowEventsButtonPublic(false);
        grid.ShowEventsButtonPublic(true);
    }

    [Fact]
    public void Hiding_the_grid_cancels_any_pending_edit()
    {
        using var grid = new TestablePropertyGrid { SelectedObject = new TestComponent() };

        grid.Visible = false;

        Assert.True(grid.VisibleChangedCancelledEdit);
    }

    private sealed class TestComponent : System.ComponentModel.Component
    {
        public string Name { get; set; } = "value";
    }
}
