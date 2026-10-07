using System.ComponentModel;
using System.Drawing;
using System.IO;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Materializes pre-read collection constants; application initialization is never invoked.</summary>
internal static class WinFormsCollectionPreview
{
    public static void Apply(WinFormsDesignerDocument document, IReadOnlyDictionary<string, Forms.Control> controls,
        List<string> problems, WinFormsResourceDocument? resources = null, string? projectPath = null)
    {
        // Read at most once per preview build, and only when an item actually refers to a
        // project resource: the preview is rebuilt after every edit, and ListEntries reads
        // Resources.resx and every file it references from disk.
        var projectResources = new Lazy<IReadOnlyList<WinFormsProjectResourceEntry>?>(
            () => LoadProjectResources(projectPath));

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
                                    EvaluateItemProperty(expression, descriptor.PropertyType, resources, projectResources));
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
                                    EvaluateItemProperty(expression, descriptor.PropertyType, resources, projectResources));
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
    /// write it as "CType(resources.GetObject(key), Type)" for a Local resource, or a plain
    /// "Global.RootNamespace.My.Resources.Resources.Name" for a Project resource file. Without
    /// this, the statement the editor wrote parses but the live preview never shows the
    /// picture, the same bug <see cref="WinFormsDesignerSession.Resources.ApplyResourceProperties"/>
    /// already fixed for ordinary controls' Local resources.
    /// </summary>
    private static object? EvaluateItemProperty(ExpressionSyntax expression, Type propertyType,
        WinFormsResourceDocument? resources, Lazy<IReadOnlyList<WinFormsProjectResourceEntry>?> projectResources)
    {
        if (WinFormsDesignerView.IsImageProperty(propertyType))
        {
            if (TryExtractResourceKey(expression) is { } key
                && resources?.TryGet(key) is { } entry
                && WinFormsDesignerResources.TryRead(entry, propertyType, out var decoded))
            {
                return decoded;
            }

            if (TryExtractProjectResourceName(expression) is { } name
                && projectResources.Value is { } entries
                && entries.FirstOrDefault(candidate => candidate.Name == name) is { } projectEntry
                && TryDecodeProjectResource(projectEntry.Bytes, propertyType, out var projectDecoded))
            {
                return projectDecoded;
            }
        }

        return WinFormsDesignerValues.Evaluate(expression, propertyType);
    }

    /// <summary>The resource name out of "Global.RootNamespace.My.Resources.Resources.Name" (or the
    /// bare "Global.My.Resources.Resources.Name" an empty RootNamespace writes), matched on the
    /// rendered text rather than walked member by member: the qualifying namespace ahead of
    /// "My.Resources.Resources" is whatever <see cref="Basalt.Workspace.WinFormsProjectResources.RootNamespace"/>
    /// returned when the assignment was written, not a shape this preview needs to parse itself.</summary>
    /// <summary>
    /// The project's image resources, or null when there are none to read. A Resources.resx
    /// this preview cannot make sense of — hand-edited, half-written, not XML at all — leaves
    /// project images unresolved in the preview instead of failing the whole form's preview,
    /// which would make every form in the project uneditable until the file was fixed.
    /// </summary>
    private static IReadOnlyList<WinFormsProjectResourceEntry>? LoadProjectResources(string? projectPath)
    {
        if (projectPath is null) return null;

        try
        {
            return WinFormsProjectResources.ListEntries(projectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Xml.XmlException or FormatException)
        {
            return null;
        }
    }

    private static string? TryExtractProjectResourceName(ExpressionSyntax expression)
    {
        const string marker = ".My.Resources.Resources.";
        var text = expression.ToString();
        if (!text.StartsWith("Global.", StringComparison.Ordinal))
        {
            return null;
        }

        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var name = text[(index + marker.Length)..];
        return name.Length > 0 ? name : null;
    }

    /// <summary>Decodes a project resource's already-read file bytes the same way
    /// <see cref="WinFormsImageResourceDialog"/>'s own preview and <see cref="WinFormsDesignerResources"/>
    /// do for a Local resource: an Icon property through <see cref="Icon"/>'s stream constructor,
    /// everything else through <see cref="Image.FromStream"/>.</summary>
    private static bool TryDecodeProjectResource(byte[] bytes, Type propertyType, out object? value)
    {
        value = null;
        try
        {
            value = propertyType.FullName == "System.Drawing.Icon"
                ? new Icon(new MemoryStream(bytes))
                : Image.FromStream(new MemoryStream(bytes));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
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
