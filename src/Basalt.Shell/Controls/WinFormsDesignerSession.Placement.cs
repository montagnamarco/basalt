using System.Drawing;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task AddControlAtAsync(Type type, string parentName, Point location)
    {
        if (!typeof(Forms.Control).IsAssignableFrom(type) || type.Assembly != typeof(Forms.Control).Assembly
            || type.IsAbstract || type == typeof(Forms.Form) || type == typeof(Forms.ContextMenuStrip))
            throw new NotSupportedException("Only built-in visual Forms controls can be placed on the surface.");
        var number = 1;
        var name = type.Name + number;
        while (Document.DeclaredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name = type.Name + ++number;
        return ApplyAsync(document => new WinFormsDesignerDocument(document.AddControl(name, "Global." + type.FullName, parentName))
            .SetProperty(name, "Location", $"New System.Drawing.Point({location.X}, {location.Y})"));
    }
}
