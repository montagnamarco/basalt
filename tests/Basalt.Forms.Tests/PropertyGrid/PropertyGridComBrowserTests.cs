using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.ComponentModel.Com2Interop;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.PropertyGrid;

public class PropertyGridComBrowserTests
{
    [AvaloniaFact]
    public void PendingCommitKeepsInvalidTextAndReportsActualSetterState()
    {
        using var grid = new System.Windows.Forms.PropertyGrid
        {
            PropertySort = PropertySort.Alphabetical, ToolbarVisible = false, HelpVisible = false, Size = new Size(240, 120),
        };
        var browser = (IComPropertyBrowser)grid;
        var model = new Model { Browser = browser };
        grid.SelectedObject = model;
        using var form = new Form { ClientSize = new Size(260, 150) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var row = grid.VisibleRowsWithDepth().Single(r => r.Item.Label == nameof(Model.Count));
        row.Item.Select();
        browser.HandleF4();
        Assert.NotNull(grid.Editor);
        grid.Editor!.Text = "invalid";
        Assert.False(browser.EnsurePendingChangesCommitted());
        Assert.Equal("invalid", grid.Editor.Text);
        grid.Editor.Text = "27";
        Assert.True(browser.EnsurePendingChangesCommitted());
        Assert.Equal(27, model.Count);
        Assert.True(model.SawPropertySet);
        Assert.False(browser.InPropertySet);
        Assert.Null(grid.Editor);
    }

    private sealed class Model
    {
        [System.ComponentModel.Browsable(false)]
        public required IComPropertyBrowser Browser { get; init; }
        [System.ComponentModel.Browsable(false)]
        public bool SawPropertySet { get; private set; }
        private int _count;
        public int Count
        {
            get => _count;
            set { SawPropertySet = Browser.InPropertySet; _count = value; }
        }
    }
}
