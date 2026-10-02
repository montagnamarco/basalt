using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStripSplitButton's two halves: the face raises ButtonClick (and
/// performs DefaultItem's own action, if one was chosen), the arrow opens
/// the drop-down (raising DropDownOpening, never ButtonClick) - plus
/// DropDownButtonWidth's own Reset and the computed SplitterBounds between them.
/// </summary>
public class SplitButtonTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static ToolStripSplitButton MakeSplitButton(ToolStrip strip)
    {
        var split = new ToolStripSplitButton("Save") { AutoSize = false, Size = new Size(60, 23) };
        split.DropDownItems.Add(new ToolStripMenuItem("Save As"));
        strip.Items.Add(split);

        return split;
    }

    [AvaloniaFact]
    public void Clicking_the_face_raises_ButtonClick_but_never_DropDownOpening()
    {
        var strip = new ToolStrip { Font = Font, Bounds = new Rectangle(0, 0, 200, 25) };
        var split = MakeSplitButton(strip);
        var form = ShowWith(strip);

        var buttonClicks = 0;
        var dropDownOpenings = 0;
        split.ButtonClick += (_, _) => buttonClicks++;
        split.DropDownOpening += (_, _) => dropDownOpenings++;

        var facePoint = new Avalonia.Point(split.Bounds.Left + 5, split.Bounds.Top + 5);
        form.Host!.MouseDown(facePoint, MouseButton.Left);
        form.Host!.MouseUp(facePoint, MouseButton.Left);

        Assert.Equal(1, buttonClicks);
        Assert.Equal(0, dropDownOpenings);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Clicking_the_arrow_raises_DropDownOpening_but_never_ButtonClick()
    {
        var strip = new ToolStrip { Font = Font, Bounds = new Rectangle(0, 0, 200, 25) };
        var split = MakeSplitButton(strip);
        var form = ShowWith(strip);

        var buttonClicks = 0;
        var dropDownOpenings = 0;
        split.ButtonClick += (_, _) => buttonClicks++;
        split.DropDownOpening += (_, _) => dropDownOpenings++;

        var arrowPoint = new Avalonia.Point(split.Bounds.Right - 3, split.Bounds.Top + 5);
        form.Host!.MouseDown(arrowPoint, MouseButton.Left);
        form.Host!.MouseUp(arrowPoint, MouseButton.Left);

        Assert.Equal(0, buttonClicks);
        Assert.Equal(1, dropDownOpenings);
        Assert.True(split.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void A_button_click_also_performs_the_chosen_DefaultItem()
    {
        var split = new ToolStripSplitButton("Save");
        var defaultItem = new ToolStripMenuItem("Save As");
        var defaultClicked = false;
        defaultItem.Click += (_, _) => defaultClicked = true;
        split.DefaultItem = defaultItem;

        split.PerformButtonClick();

        Assert.True(defaultClicked);
    }

    [Fact]
    public void Setting_DefaultItem_raises_DefaultItemChanged()
    {
        var split = new ToolStripSplitButton("Save");
        var raised = 0;
        split.DefaultItemChanged += (_, _) => raised++;

        split.DefaultItem = new ToolStripMenuItem("Save As");

        Assert.Equal(1, raised);

        // Setting the same value again must not raise a second time.
        split.DefaultItem = split.DefaultItem;
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResetDropDownButtonWidth_restores_the_original_11_pixel_width()
    {
        var split = new ToolStripSplitButton("Save") { DropDownButtonWidth = 40 };

        split.ResetDropDownButtonWidth();

        Assert.Equal(11, split.DropDownButtonWidth);
    }

    [Fact]
    public void SplitterBounds_sits_exactly_between_the_button_and_the_arrow()
    {
        var split = new ToolStripSplitButton("Save") { AutoSize = false, Size = new Size(60, 23) };

        Assert.Equal(split.ButtonBounds.Right, split.SplitterBounds.Right);
        Assert.Equal(split.DropDownButtonBounds.Left, split.SplitterBounds.Right);
    }

    // --- Nested in an open menu: DismissWhenClicked decides whether the ancestor closes too ----------

    [AvaloniaFact]
    public void A_split_button_nested_in_an_open_menu_the_arrow_leaves_the_menu_open_the_face_closes_it()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var split = new ToolStripSplitButton("Save") { AutoSize = false, Size = new Size(60, 23) };
        split.DropDownItems.Add(new ToolStripMenuItem("Save As"));
        file.DropDownItems.Add(split);
        menu.Items.Add(file);
        var form = ShowWith(menu);

        var point = new Avalonia.Point(file.Bounds.Left + 5, file.Bounds.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        Assert.True(file.DropDown.IsOpen);

        // Clicking the arrow half only ever opens the split button's own submenu: the menu it sits in stays open.
        var dropDownWindow = file.DropDown.PopupWindow!;
        var arrowPoint = new Avalonia.Point(split.Bounds.Right - 3, split.Bounds.Top + 5);
        dropDownWindow.MouseDown(arrowPoint, MouseButton.Left);
        dropDownWindow.MouseUp(arrowPoint, MouseButton.Left);

        Assert.True(split.DropDown.IsOpen);
        Assert.True(file.DropDown.IsOpen);

        split.DropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

        // Clicking the face performs the button's action and closes the whole chain, same as any other leaf item.
        var facePoint = new Avalonia.Point(split.Bounds.Left + 5, split.Bounds.Top + 5);
        dropDownWindow.MouseDown(facePoint, MouseButton.Left);
        dropDownWindow.MouseUp(facePoint, MouseButton.Left);

        Assert.False(file.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
