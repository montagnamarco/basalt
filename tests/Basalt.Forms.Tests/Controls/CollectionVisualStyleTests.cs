using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

public sealed class CollectionVisualStyleTests
{
    [AvaloniaFact]
    public void ListAndCheckedListPaintScopedSelectionDisabledStateAndGlyphs()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = ThemedForm(platform);
            var palette = PortableVisualStyles.Palette(platform);
            var list = new ListBox { Size = new Size(180, 70) };
            list.Items.AddRange(["Alpha", "Beta"]);
            list.SelectedIndex = 0;
            form.Controls.Add(list);

            using (var painted = Draw(list))
            {
                AssertColor(painted, 150, painted.Height - 4, palette.Field);
                AssertColor(painted, 150, list.ItemHeight / 2, palette.HotFace);
            }

            list.Enabled = false;
            using (var disabled = Draw(list))
            {
                AssertColor(disabled, 150, disabled.Height - 4, palette.Face);
                AssertColor(disabled, 150, list.ItemHeight / 2, palette.PressedFace);
            }

            var checkedList = new CheckedListBox { Size = new Size(180, 70), Location = new Point(0, 80) };
            checkedList.Items.Add("Checked");
            checkedList.SetItemChecked(0, true);
            form.Controls.Add(checkedList);
            using var checkedImage = Draw(checkedList);
            Assert.True(ContainsColor(checkedImage, new Rectangle(2, 0, 14, checkedList.ItemHeight), palette.Accent),
                $"The {platform} checked item must paint a themed accent glyph.");

            checkedList.Enabled = false;
            using (var disabledCheck = Draw(checkedList))
            {
                Assert.True(ContainsColor(disabledCheck,
                    new Rectangle(2, 0, 14, checkedList.ItemHeight), palette.DisabledText));
            }

            list.Enabled = true;
            form.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(list.Focus());
            using var focused = Draw(list);
            AssertColor(focused, 150, list.ItemHeight / 2, palette.Selection);
        }
    }

    [AvaloniaFact]
    public void ComboBoxPaintsProfileFrameArrowAndFocusedSelection()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = ThemedForm(platform);
            var palette = PortableVisualStyles.Palette(platform);
            var combo = new ComboBox
            {
                Size = new Size(160, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            combo.Items.Add("Choice");
            combo.SelectedIndex = 0;
            form.Controls.Add(combo);

            using (var inactive = Draw(combo))
            {
                AssertColor(inactive, 0, 14, palette.Border);
                AssertColor(inactive, 100, 14, palette.Field);
            }

            form.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(combo.Focus());
            using var focused = Draw(combo);
            AssertColor(focused, 0, 14, palette.Accent);
            AssertColor(focused, 100, 14, palette.Selection);

            combo.Enabled = false;
            using var disabled = Draw(combo);
            Assert.True(ContainsColor(disabled, new Rectangle(130, 1, 28, 26), palette.DisabledText),
                $"The {platform} disabled arrow must use the disabled text color. Colors: {ColorsIn(disabled, new Rectangle(130, 1, 28, 26))}");
        }
    }

    [AvaloniaFact]
    public void ListViewAndTreeViewUseProfileHeadersSelectionAndCheckmarks()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = ThemedForm(platform);
            var palette = PortableVisualStyles.Palette(platform);
            var list = new ListView
            {
                Size = new Size(220, 100),
                View = View.Details,
                CheckBoxes = true,
            };
            list.Columns.Add("Name", 100);
            var item = list.Items.Add("Entry");
            item.Checked = true;
            item.Selected = true;
            form.Controls.Add(list);

            using (var painted = Draw(list))
            {
                Assert.True(ContainsColor(painted, new Rectangle(98, 3, 8, 8), palette.Border),
                    $"The {platform} column divider must use the profile border color.");
                var row = list.GetItemRect(0);
                AssertColor(painted, 80, row.Top + row.Height / 2, palette.HotFace);
                Assert.True(ContainsColor(painted,
                    new Rectangle(0, row.Top, 19, row.Height), palette.Accent));
            }

            list.Enabled = false;
            using (var disabled = Draw(list))
            {
                var row = list.GetItemRect(0);
                AssertColor(disabled, 80, row.Top + row.Height / 2, palette.PressedFace);
            }

            var tree = new TreeView
            {
                Size = new Size(220, 100),
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
            };
            var root = tree.Nodes.Add("Root");
            root.Nodes.Add("Child");
            root.Checked = true;
            tree.SelectedNode = root;
            form.Controls.Add(tree);

            using var treeImage = Draw(tree);
            AssertColor(treeImage, 180, tree.ItemHeight / 2, palette.HotFace);
            Assert.True(ContainsColor(treeImage,
                new Rectangle(0, 0, 60, tree.ItemHeight), palette.Accent),
                $"The {platform} checked tree node must paint a themed accent glyph.");

            tree.Enabled = false;
            using var disabledTree = Draw(tree);
            AssertColor(disabledTree, 180, tree.ItemHeight / 2, palette.PressedFace);
        }
    }

    [AvaloniaFact]
    public void ExplicitAmbientColorsAndFontSurviveCollectionProfiles()
    {
        using var font = new Font("Tahoma", 12);
        using var form = ThemedForm(PortableVisualStylePlatform.Linux);
        form.BackColor = Color.Magenta;
        form.ForeColor = Color.Green;
        form.Font = font;

        Control[] controls =
        [
            new ListBox { Size = new Size(160, 60) },
            new CheckedListBox { Size = new Size(160, 60) },
            new ComboBox { Size = new Size(160, 28) },
            new ListView { Size = new Size(160, 60) },
            new TreeView { Size = new Size(160, 60) },
        ];

        foreach (var control in controls)
        {
            form.Controls.Add(control);
            using var bitmap = Draw(control);
            Assert.True(bitmap.GetPixel(80, control.Height / 2).ToArgb() == Color.Magenta.ToArgb(),
                $"{control.GetType().Name} inherited background is {bitmap.GetPixel(80, control.Height / 2)} rather than Magenta.");
            Assert.Equal(Color.Green, control.ForeColor);
            Assert.Same(font, control.Font);
        }
    }

    [AvaloniaFact]
    public void ClassicOptOutKeepsSystemSelectionInsteadOfThemedSelection()
    {
        using var form = new Form
        {
            VisualStylesOverride = false,
            VisualStylePlatformOverride = PortableVisualStylePlatform.Linux,
        };
        var list = new ListBox { Size = new Size(180, 70) };
        list.Items.Add("Item");
        list.SelectedIndex = 0;
        form.Controls.Add(list);

        using var classic = Draw(list);
        AssertColor(classic, 150, list.ItemHeight / 2, SystemColors.Highlight);
        AssertColor(classic, 150, classic.Height - 4, SystemColors.Window);
    }

    private static Form ThemedForm(PortableVisualStylePlatform platform) => new()
    {
        VisualStylesOverride = true,
        VisualStylePlatformOverride = platform,
    };

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    private static void AssertColor(Bitmap bitmap, int x, int y, Color expected) =>
        Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, y).ToArgb());

    private static bool ContainsColor(Bitmap bitmap, Rectangle bounds, Color expected)
    {
        for (var y = bounds.Top; y < Math.Min(bounds.Bottom, bitmap.Height); y++)
        {
            for (var x = bounds.Left; x < Math.Min(bounds.Right, bitmap.Width); x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ColorsIn(Bitmap bitmap, Rectangle bounds)
    {
        var colors = new HashSet<int>();
        for (var y = bounds.Top; y < Math.Min(bounds.Bottom, bitmap.Height); y++)
        {
            for (var x = bounds.Left; x < Math.Min(bounds.Right, bitmap.Width); x++)
            {
                colors.Add(bitmap.GetPixel(x, y).ToArgb());
            }
        }

        return string.Join(", ", colors.Select(argb => Color.FromArgb(argb).ToString()));
    }
}
