using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>LabelEdit: BeginEdit, BeforeLabelEdit/AfterLabelEdit, F2 and the click-to-edit-selected-item gesture, Enter/Escape.</summary>
public class ListViewLabelEditTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form Show(ListView list)
    {
        list.Font = Font;
        list.Bounds = new Rectangle(10, 10, 200, 150);
        var form = new Form { ClientSize = new Size(300, 300) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static void Press(Form form, PhysicalKey key)
    {
        form.Host!.KeyPressQwerty(key, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static TextBox? FindEditBox(ListView list) => list.Controls.OfType<TextBox>().FirstOrDefault();

    [Fact]
    public void BeginEdit_opens_a_text_box_prefilled_with_the_items_text()
    {
        var list = new ListView { LabelEdit = true, View = View.List, Font = Font, Bounds = new Rectangle(0, 0, 200, 100) };
        var item = list.Items.Add("Original");

        item.BeginEdit();

        var box = FindEditBox(list);
        Assert.NotNull(box);
        Assert.Equal("Original", box!.Text);
        Assert.True(box.Visible);
    }

    [Fact]
    public void BeforeLabelEdit_setting_CancelEdit_keeps_the_text_box_closed()
    {
        var list = new ListView { LabelEdit = true, View = View.List, Font = Font, Bounds = new Rectangle(0, 0, 200, 100) };
        var item = list.Items.Add("Original");
        list.BeforeLabelEdit += (_, e) => e.CancelEdit = true;

        item.BeginEdit();

        Assert.Null(FindEditBox(list));
    }

    [AvaloniaFact]
    public void F2_on_the_focused_item_opens_the_edit_box()
    {
        var list = new ListView { LabelEdit = true, View = View.List };
        var item = list.Items.Add("Original");
        var form = Show(list);
        list.FocusedItem = item;
        list.Focus();

        Press(form, PhysicalKey.F2);

        Assert.NotNull(FindEditBox(list));
        form.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_the_typed_text_through_AfterLabelEdit()
    {
        var list = new ListView { LabelEdit = true, View = View.List };
        var item = list.Items.Add("Original");
        var form = Show(list);
        string? committedLabel = null;
        list.AfterLabelEdit += (_, e) => committedLabel = e.Label;

        item.BeginEdit();
        var box = FindEditBox(list)!;
        box.Text = "Renamed";
        box.Focus();

        Press(form, PhysicalKey.Enter);

        Assert.Equal("Renamed", committedLabel);
        Assert.Equal("Renamed", item.Text);
        Assert.Null(FindEditBox(list));
        form.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_without_changing_the_items_text()
    {
        var list = new ListView { LabelEdit = true, View = View.List };
        var item = list.Items.Add("Original");
        var form = Show(list);
        var afterRaised = false;
        list.AfterLabelEdit += (_, e) => afterRaised = true;

        item.BeginEdit();
        var box = FindEditBox(list)!;
        box.Text = "Should not stick";
        box.Focus();

        Press(form, PhysicalKey.Escape);

        Assert.True(afterRaised);
        Assert.Equal("Original", item.Text);
        Assert.Null(FindEditBox(list));
        form.Close();
    }

    [AvaloniaFact]
    public void Disposing_the_ListView_after_an_edit_finished_also_disposes_the_edit_box()
    {
        var list = new ListView { LabelEdit = true, View = View.List };
        var item = list.Items.Add("Original");
        var form = Show(list);

        // BeginEdit/EndEditCore create and then remove _editBox from Controls, but never dispose
        // it: it stays alive, ready to be reused by the next edit, until the ListView itself goes.
        item.BeginEdit();
        var box = FindEditBox(list)!;
        box.Focus();
        Press(form, PhysicalKey.Enter);

        Assert.Null(FindEditBox(list));
        Assert.False(box.IsDisposed);

        list.Dispose();

        Assert.True(box.IsDisposed);
        form.Close();
    }
}
