using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Materializes pre-read collection constants; application initialization is never invoked.</summary>
internal static class WinFormsCollectionPreview
{
    public static void Apply(WinFormsDesignerDocument document, IReadOnlyDictionary<string, Forms.Control> controls,
        List<string> problems, WinFormsResourceDocument? resources = null)
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
                                    EvaluateItemProperty(expression, descriptor.PropertyType, resources));
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
            // A plain ToolStrip or StatusStrip (also a ToolStrip subclass, but not a MenuStrip,
            // which the branch above already materialized) owns a flat Items collection the
            // "Type Here" box edits; without this the real control never shows what the inline
            // editor just added, even though the serialized source is correct.
            if (control is Forms.ToolStrip strip && strip is not Forms.MenuStrip)
            {
                try
                {
                    var toolStripDocument = document.ToolStripItems;
                    foreach (var item in toolStripDocument.Read(name))
                    {
                        var itemType = typeof(Forms.ToolStripItem).Assembly.GetType(
                            "System.Windows.Forms." + item.TypeName, false, true);
                        if (itemType is null)
                        {
                            problems.Add(item.Name + ": " + item.TypeName);
                            continue;
                        }
                        var created = (Forms.ToolStripItem)Activator.CreateInstance(itemType)!;
                        created.Name = item.Name;
                        foreach (var (property, expression) in toolStripDocument.ReadItemProperties(name, item.Name))
                        {
                            if (property.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                            var descriptor = TypeDescriptor.GetProperties(created)[property];
                            if (descriptor is null || descriptor.IsReadOnly) continue;
                            try
                            {
                                descriptor.SetValue(created,
                                    EvaluateItemProperty(expression, descriptor.PropertyType, resources));
                            }
                            catch
                            {
                                problems.Add(item.Name + "." + property + ": " +
                                    Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                            }
                        }
                        strip.Items.Add(created);

                        // A DropDownButton/SplitButton also owns a cascading DropDownItems tree,
                        // edited and modeled the same way a MenuStrip's own Items/DropDownItems
                        // are (see WinFormsMenuDocument): materialize it here too, isolated in its
                        // own file so this loop only grows by this one call-out.
                        if (created is Forms.ToolStripDropDownItem dropDownOwner &&
                            WinFormsMenuDocument.IsDropDownOwnerType(item.TypeName))
                        {
                            try { WinFormsToolStripDropDownPreview.Apply(document.Menus, item.Name, dropDownOwner, problems, resources); }
                            catch (NotSupportedException)
                            {
                                problems.Add(item.Name + ".DropDownItems: " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                            }
                        }
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

    /// <summary>
    /// A MenuStrip/ToolStrip item's Image or Icon is never a literal <see cref="WinFormsDesignerValues"/>
    /// can parse: Visual Studio (and <see cref="Basalt.Workspace.WinFormsDesignerDocument.SetImageResource"/>)
    /// write it as "CType(resources.GetObject(key), Type)". Without this, the statement the editor wrote
    /// parses but the live preview never shows the picture, the same bug
    /// <see cref="WinFormsDesignerSession.Resources.ApplyResourceProperties"/> already fixed for ordinary
    /// controls.
    /// </summary>
    private static object? EvaluateItemProperty(ExpressionSyntax expression, Type propertyType, WinFormsResourceDocument? resources)
    {
        if (WinFormsDesignerView.IsImageProperty(propertyType)
            && TryExtractResourceKey(expression) is { } key
            && resources?.TryGet(key) is { } entry
            && WinFormsDesignerResources.TryRead(entry, propertyType, out var decoded))
        {
            return decoded;
        }

        return WinFormsDesignerValues.Evaluate(expression, propertyType);
    }

    /// <summary>The resource key out of "CType(resources.GetObject("key"), Type)", however it is wrapped or cast.</summary>
    private static string? TryExtractResourceKey(ExpressionSyntax expression)
    {
        var inner = expression switch
        {
            CTypeExpressionSyntax cast => cast.Expression,
            DirectCastExpressionSyntax cast => cast.Expression,
            ParenthesizedExpressionSyntax parentheses => parentheses.Expression,
            _ => expression,
        };
        if (!ReferenceEquals(inner, expression)) return TryExtractResourceKey(inner);

        if (expression is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax method,
            } invocation
            || method.Name.Identifier.ValueText is not ("GetObject" or "GetString"))
        {
            return null;
        }

        var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray();
        return arguments is [{ Expression: LiteralExpressionSyntax { Token.Value: string key } }] ? key : null;
    }
}
