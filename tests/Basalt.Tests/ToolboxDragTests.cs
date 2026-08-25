using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Designer.Toolbox;
using Avalonia.LogicalTree;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// Getting a control from the toolbox onto the surface.
/// </summary>
/// <remarks>
/// It answered only to a double-click, with nothing on screen saying so — a
/// toolbox that looks like it does nothing. Dragging is the gesture people
/// arrive expecting, and it says where the control lands.
/// </remarks>
public sealed class ToolboxDragTests
{
    [AvaloniaFact]
    public void TheFormatCarriesTheEntryItself()
    {
        // In-process: the entry is a live object handed to the surface in the
        // same application, not bytes for another program to read.
        var item = new ToolboxItem("Button", "Button", "Common", "<Button />");

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ToolboxPanel.DragFormat, item));

        // Through IDataTransfer explicitly: the extension exists on both the
        // sync and async interfaces, and DataTransfer implements both.
        var data = (IDataTransfer)transfer;

        Assert.True(data.Contains(ToolboxPanel.DragFormat));
        Assert.Same(item, data.TryGetValue(ToolboxPanel.DragFormat));
    }

    [AvaloniaFact]
    public void ThePanelSaysHowToUseIt()
    {
        // The hint is the whole reason the feature is findable: a list of
        // names with no instruction reads as a list, not as a source of
        // controls.
        var panel = new ToolboxPanel();

        var hint = panel.GetLogicalDescendants()
            .OfType<Avalonia.Controls.TextBlock>()
            .Select(t => t.Text ?? "")
            .FirstOrDefault(t => t.Length > 0);

        Assert.False(string.IsNullOrWhiteSpace(hint), "the toolbox explains nothing");
    }

    [AvaloniaFact]
    public void OneDragImagePerPasteboardEntry()
    {
        // What AppKit actually counts, and what it terminates the process
        // over: "There are 1 items on the pasteboard, but 2 drag images."
        //
        // macOS draws one image per transfer item but writes a pasteboard
        // entry only for items carrying a format it can serialise — an
        // in-process format is not one. Two items therefore meant two images
        // and one entry. One item holding both formats is one of each.
        var item = new DataTransferItem();
        item.Set(ToolboxPanel.DragFormat, new ToolboxItem("Button", "Button", "Common", "<Button />"));
        item.SetText("<Button />");

        var transfer = new DataTransfer();
        transfer.Add(item);

        Assert.Single(transfer.Items);

        // At least one format the platform can write, or the pasteboard is
        // empty while an image is still drawn.
        Assert.Contains(item.Formats, f => f.Kind != DataFormatKind.InProcess);
    }

    [AvaloniaFact]
    public void TheDragStillCarriesTheEntryItself()
    {
        var chosen = new ToolboxItem("Button", "Button", "Common", "<Button />");

        var item = new DataTransferItem();
        item.Set(ToolboxPanel.DragFormat, chosen);
        item.SetText(chosen.DefaultXaml);

        var transfer = new DataTransfer();
        transfer.Add(item);

        var data = (IDataTransfer)transfer;

        Assert.Same(chosen, data.TryGetValue(ToolboxPanel.DragFormat));
        Assert.Equal("<Button />", data.TryGetText());
    }
}
