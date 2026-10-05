using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Materializes pre-read collection constants; application initialization is never invoked.</summary>
internal static class WinFormsCollectionPreview
{
    public static void Apply(WinFormsDesignerDocument document, IReadOnlyDictionary<string, Forms.Control> controls, List<string> problems)
    {
        foreach (var (name, control) in controls)
        {
            if (control is Forms.MenuStrip menu)
            {
                try
                {
                    var menuDocument = document.Menus;
                    var definitions = menuDocument.Read(name);
                    var items = definitions.ToDictionary(item => item.Name, item => item.IsSeparator
                        ? (Forms.ToolStripItem)new Forms.ToolStripSeparator() { Name = item.Name }
                        : new Forms.ToolStripMenuItem(item.Text) { Name = item.Name }, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in definitions)
                    {
                        foreach (var (property, expression) in menuDocument.ReadItemProperties(name, item.Name))
                        {
                            if (property.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                            var descriptor = TypeDescriptor.GetProperties(items[item.Name])[property];
                            if (descriptor is null || descriptor.IsReadOnly) continue;
                            try
                            {
                                descriptor.SetValue(items[item.Name],
                                    WinFormsDesignerValues.Evaluate(expression, descriptor.PropertyType));
                            }
                            catch
                            {
                                problems.Add(item.Name + "." + property + ": " +
                                    Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                            }
                        }

                        if (item.Parent.Equals(name, StringComparison.OrdinalIgnoreCase)) menu.Items.Add(items[item.Name]);
                        else ((Forms.ToolStripMenuItem)items[item.Parent]).DropDownItems.Add(items[item.Name]);
                    }
                }
                catch (NotSupportedException) { problems.Add(name + ".Items: " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported)); }
            }
            if (control is Forms.ListBox or Forms.ComboBox)
            {
                var state = document.Collections.ReadItems(name);
                if (!state.IsSupported) { problems.Add(name + ".Items: " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported)); continue; }
                foreach (var item in state.Items)
                {
                    if (control is Forms.ListBox list) list.Items.Add(item);
                    else ((Forms.ComboBox)control).Items.Add(item);
                }
            }
            if (control is not Forms.DataGridView grid) continue;
            var columns = document.Collections.ReadColumns(name);
            if (!columns.IsSupported) { problems.Add(name + ".Columns: " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported)); continue; }
            foreach (var definition in columns.Columns)
            {
                var type = typeof(Forms.DataGridViewColumn).Assembly.GetType("System.Windows.Forms." + definition.TypeName)!;
                var column = (Forms.DataGridViewColumn)Activator.CreateInstance(type)!;
                column.Name = definition.Name;
                column.HeaderText = definition.HeaderText;
                column.Width = definition.Width;
                foreach (var (property, expression) in document.Collections.ReadColumnProperties(definition.Name))
                {
                    var descriptor = TypeDescriptor.GetProperties(column)[property];
                    if (descriptor is null || descriptor.IsReadOnly) continue;
                    try { descriptor.SetValue(column, WinFormsDesignerValues.Evaluate(expression, descriptor.PropertyType)); }
                    catch { problems.Add(definition.Name + "." + property + ": " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported)); }
                }
                grid.Columns.Add(column);
            }
        }
    }
}
