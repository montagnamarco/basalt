using System.Drawing;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task AddControlAtAsync(Type type, string parentName, Point location)
    {
        if (!typeof(Forms.Control).IsAssignableFrom(type) || !WinFormsDesignTypes.IsDesignable(type, _designContext)
            || type.IsAbstract || typeof(Forms.Form).IsAssignableFrom(type) || type == typeof(Forms.ContextMenuStrip))
            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerNotDesignable));
        var number = 1;
        var name = type.Name + number;
        while (Document.DeclaredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name = type.Name + ++number;
        return ApplyControlEditAsync(document => new WinFormsDesignerDocument(document.AddControl(name, "Global." + type.FullName, parentName))
            .SetProperty(name, "Location", $"New System.Drawing.Point({location.X}, {location.Y})"));
    }

    /// <summary>The panel names the designer writes for the Forms containers that hold children.</summary>
    internal static readonly string[] PanelNames =
        ["Panel1", "Panel2", "ContentPanel", "TopToolStripPanel", "BottomToolStripPanel", "LeftToolStripPanel", "RightToolStripPanel"];

    /// <summary>
    /// The control that holds a child of an owner: the owner's panel when the designer names one, otherwise the owner. Only the
    /// panels of the Forms containers are read, typed, so no project property runs: a getter that throws, or a member that
    /// shadows the panel, cannot stop the preview. An unknown panel name holds the child in the owner.
    /// </summary>
    internal static Forms.Control HolderOf(Forms.Control owner, string? panel) => (owner, panel) switch
    {
        (Forms.SplitContainer split, "Panel1") => split.Panel1,
        (Forms.SplitContainer split, "Panel2") => split.Panel2,
        (Forms.ToolStripContainer container, "ContentPanel") => container.ContentPanel,
        (Forms.ToolStripContainer container, "TopToolStripPanel") => container.TopToolStripPanel,
        (Forms.ToolStripContainer container, "BottomToolStripPanel") => container.BottomToolStripPanel,
        (Forms.ToolStripContainer container, "LeftToolStripPanel") => container.LeftToolStripPanel,
        (Forms.ToolStripContainer container, "RightToolStripPanel") => container.RightToolStripPanel,
        _ => owner,
    };

    /// <summary>
    /// Adds a control where the designer's code puts it: into the container's panel when it has one (SplitContainer1.Panel1),
    /// and into its TableLayoutPanel cell when the code gives the column and row.
    /// </summary>
    private static void AddToPlacement(Forms.Control owner, WinFormsDesignerControl model, Forms.Control control, List<string> problems)
    {
        var holder = HolderOf(owner, model.ParentPanel);
        if (model.Cell is { } cell && holder is Forms.TableLayoutPanel table)
        {
            // A cell that the panel cannot hold is reported as a problem of the designer's code. Any other failure still throws.
            try { table.Controls.Add(control, cell.Column, cell.Row); }
            catch (ArgumentException exception) { problems.Add(model.Name + ": " + exception.GetBaseException().Message); }
        }
        else holder.Controls.Add(control);
    }
}

