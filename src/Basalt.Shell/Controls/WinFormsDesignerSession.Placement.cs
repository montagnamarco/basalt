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
}
