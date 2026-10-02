using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>DrawMode OwnerDrawText/OwnerDrawAll hand the row (or just its label) to DrawNode.</summary>
public class TreeViewOwnerDrawTests
{
    private static Form ShowWith(TreeView tree)
    {
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(tree);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void OwnerDrawAll_raises_DrawNode_with_the_whole_row()
    {
        var tree = new TreeView { DrawMode = TreeViewDrawMode.OwnerDrawAll };
        var node = tree.Nodes.Add("Alpha");
        DrawTreeNodeEventArgs? seen = null;
        tree.DrawNode += (_, e) => seen = e;
        var form = ShowWith(tree);

        using var bitmap = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(bitmap, new Rectangle(0, 0, tree.Width, tree.Height));

        Assert.NotNull(seen);
        Assert.Equal(node, seen!.Node);
        Assert.Equal(tree.ClientSize.Width, seen.Bounds.Width);

        form.Close();
    }

    [AvaloniaFact]
    public void Normal_DrawMode_never_raises_DrawNode()
    {
        var tree = new TreeView { DrawMode = TreeViewDrawMode.Normal };
        tree.Nodes.Add("Alpha");
        var raised = false;
        tree.DrawNode += (_, _) => raised = true;
        var form = ShowWith(tree);

        using var bitmap = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(bitmap, new Rectangle(0, 0, tree.Width, tree.Height));

        Assert.False(raised);

        form.Close();
    }

    [AvaloniaFact]
    public void OwnerDrawAll_without_DrawDefault_skips_the_built_in_painting()
    {
        var tree = new TreeView { DrawMode = TreeViewDrawMode.OwnerDrawAll, BackColor = Color.White };
        tree.Nodes.Add("Alpha");
        var row = Rectangle.Empty;
        tree.DrawNode += (_, e) =>
        {
            row = e.Bounds;
            using var brush = new SolidBrush(Color.Red);
            e.Graphics.FillRectangle(brush, e.Bounds);
        };
        var form = ShowWith(tree);

        using var bitmap = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(bitmap, new Rectangle(0, 0, tree.Width, tree.Height));

        // Every pixel of the row stays the handler's red: no label text, lines or glyphs drawn over it.
        // The row is in client coordinates; the bitmap also holds the 3D border around them.
        Assert.False(row.IsEmpty);
        var border = (tree.Width - tree.ClientSize.Width) / 2;
        row.Offset(border, border);
        var notRed = 0;
        for (var y = row.Top; y < row.Bottom; y++)
        {
            for (var x = row.Left; x < row.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != Color.Red.ToArgb()) notRed++;
            }
        }

        Assert.Equal(0, notRed);

        form.Close();
    }
}
