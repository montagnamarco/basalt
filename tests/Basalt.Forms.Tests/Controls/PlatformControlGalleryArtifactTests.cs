using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// Not a pixel-accuracy suite: every control here (button face, checkbox/radio
/// glyph, selection colour, grid, tree, tabs...) already has a focused per-platform
/// pixel test elsewhere (<c>PortableVisualStyleTests</c>,
/// <c>PortableVisualStyleInputControlsTests</c>, <c>RangeAndFieldVisualStyleTests</c>,
/// <c>CollectionVisualStyleTests</c>, <c>PropertyGridAndRichTextVisualStyleTests</c>).
/// Asserting the same colours again here per control would just duplicate those
/// without adding coverage. What this class is for: it builds one form with every
/// portable control side by side per platform profile and saves it as a PNG, so a
/// person (or a future test) can look at the whole gallery at once. The one pixel
/// assertion below is a smoke test that painting actually ran end to end, not a
/// claim about any one control's appearance.
/// </summary>
public sealed class PlatformControlGalleryArtifactTests
{
    [AvaloniaFact]
    public void CommonControlsRenderTogetherInEachPlatformProfileAndTheGalleryIsSavedForReview()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateGallery(platform);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            Assert.Equal(PortableVisualStyles.Palette(platform).Surface.ToArgb(), bitmap.GetPixel(755, 590).ToArgb());
            bitmap.Save(Path.Combine(Path.GetTempPath(), $"basalt-control-gallery-{platform}.png"), ImageFormat.Png);
        }
    }

    private static Form CreateGallery(PortableVisualStylePlatform platform)
    {
        var form = new Form
        {
            VisualStylesOverride = true,
            VisualStylePlatformOverride = platform,
            FormBorderStyle = FormBorderStyle.None,
            Size = new Size(780, 620)
        };
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(new ToolStripMenuItem("Open"));
        menu.Items.Add(file);
        menu.Items.Add(new ToolStripMenuItem("Edit"));
        form.Controls.Add(menu);
        Add(form, new Label { Text = $"{platform} — portable controls", AutoSize = false }, 16, 36, 400, 24);
        Add(form, new Button { Text = "Default action" }, 16, 70, 140, 30);
        Add(form, new Button { Text = "Disabled", Enabled = false }, 166, 70, 130, 30);
        Add(form, new CheckBox { Text = "Enabled", Checked = true }, 16, 110, 140, 26);
        Add(form, new RadioButton { Text = "Selected", Checked = true }, 166, 110, 140, 26);
        Add(form, new TextBox { Text = "Editable text", AutoSize = false }, 16, 150, 280, 28);
        Add(form, new TextBox { Text = "Read-only text", ReadOnly = true, AutoSize = false }, 16, 188, 280, 28);
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(["First option", "Second option"]);
        combo.SelectedIndex = 0;
        Add(form, combo, 16, 226, 280, 28);
        Add(form, new NumericUpDown { Value = 42 }, 16, 264, 130, 28);
        Add(form, new DateTimePicker { Value = new DateTime(2026, 10, 4), Format = DateTimePickerFormat.Short }, 156, 264, 140, 28);
        Add(form, new ProgressBar { Value = 65 }, 16, 306, 280, 22);
        Add(form, new TrackBar { Value = 6, TickStyle = TickStyle.None }, 16, 338, 280, 45);
        Add(form, new HScrollBar { Value = 30 }, 16, 390, 280, 18);
        var list = new ListBox();
        list.Items.AddRange(["First item", "Selected item", "Third item"]);
        list.SelectedIndex = 1;
        Add(form, list, 316, 70, 200, 100);
        var checkedList = new CheckedListBox();
        checkedList.Items.Add("Checked item", true);
        checkedList.Items.Add("Unchecked item", false);
        Add(form, checkedList, 316, 184, 200, 78);
        var tree = new TreeView();
        var parent = tree.Nodes.Add("Project");
        parent.Nodes.Add("MainForm.vb");
        parent.Nodes.Add("Resources.resx");
        parent.Expand();
        Add(form, tree, 536, 70, 224, 100);
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 120);
        listView.Columns.Add("Value", 80);
        listView.Items.Add(new ListViewItem(["Example", "42"]));
        Add(form, listView, 536, 184, 224, 78);
        var tabs = new TabControl();
        tabs.TabPages.Add(new TabPage("General"));
        tabs.TabPages.Add(new TabPage("Details"));
        tabs.TabPages[0].Controls.Add(new Label { Text = "Tab content", Location = new Point(12, 16), AutoSize = true });
        Add(form, tabs, 316, 282, 444, 126);
        var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false, RowHeadersVisible = false };
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("Value", "Value");
        grid.Rows.Add("Alpha", "1");
        grid.Rows.Add("Beta", "2");
        Add(form, grid, 16, 430, 500, 145);
        var group = new GroupBox { Text = "Options" };
        group.Controls.Add(new CheckBox { Text = "Remember", Checked = true, Bounds = new Rectangle(12, 30, 160, 25) });
        Add(form, group, 536, 430, 224, 145);
        var status = new StatusStrip();
        status.Items.Add(new ToolStripStatusLabel("Ready"));
        form.Controls.Add(status);
        form.AcceptButton = (Button)form.Controls.OfType<Button>().First();
        form.PerformLayout();
        return form;
    }

    private static void Add(Form form, Control control, int x, int y, int width, int height)
    {
        control.Bounds = new Rectangle(x, y, width, height);
        form.Controls.Add(control);
    }
}
