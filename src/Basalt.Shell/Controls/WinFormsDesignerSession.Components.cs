using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Reflection;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    private readonly Dictionary<string, IComponent> _components = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IComponent> Components => _components;

    public IComponent? FindComponent(string name) => _components.GetValueOrDefault(name);

    public string NameOfComponent(IComponent component) =>
        _components.First(pair => ReferenceEquals(pair.Value, component)).Key;

    public bool IsComponentPropertyExplicit(IComponent component, string property) =>
        ComponentModel(component).Properties.ContainsKey(property);

    public string? ExplicitComponentValue(IComponent component, string property)
    {
        var model = ComponentModel(component);
        if (!model.PropertySyntax.TryGetValue(property, out var syntax)) return null;

        // The preview does not start timers or show popup menus. Show the stored
        // constant in the editor instead of the inert preview value.
        if (IsInertPreviewProperty(component, property))
        {
            try
            {
                var value = WinFormsDesignerValues.Evaluate(syntax, typeof(bool));
                return TypeDescriptor.GetConverter(typeof(bool)).ConvertToInvariantString(value);
            }
            catch { return model.Properties[property]; }
        }

        if (component is SerialPort)
        {
            try { return ReadPreviewProperty(component, property); }
            catch (InvalidOperationException) { return model.Properties[property]; }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
            { return model.Properties[property]; }
        }

        return ReadPreviewProperty(component, property);
    }

    public async Task SetComponentPropertyAsync(IComponent component, string property, string? text)
    {
        var name = NameOfComponent(component);
        string? expression = null;
        if (text is not null)
        {
            var descriptor = TypeDescriptor.GetProperties(component)[property]
                ?? throw new ArgumentException("The property does not exist.", nameof(property));
            var value = descriptor.Converter.ConvertFromInvariantString(text);
            try { expression = WinFormsDesignerValues.Serialize(value); }
            finally { if (value is Font { IsSystemFont: false } font) font.Dispose(); }
        }
        await ApplyAsync(document => document.SetComponentProperty(name, property, expression));
    }

    public Task AddComponentAsync(Type type)
    {
        if (!IsSupportedComponentType(type))
            throw new NotSupportedException("Only built-in Forms tray components are available in this toolbox.");
        var number = 1;
        var name = type.Name + number;
        var used = Document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (used.Contains(name)) name = type.Name + ++number;
        return ApplyAsync(document => document.AddComponent(name, "Global." + type.FullName));
    }

    public Task DeleteComponentAsync(IComponent component, Action? commitCompanion = null,
        Action? undoCompanion = null, Action? redoCompanion = null) =>
        ApplyAsync(document => document.RemoveComponent(NameOfComponent(component)),
            commitCompanion, undoCompanion, redoCompanion);

    internal void BuildComponentPreviews(List<string> problems)
    {
        foreach (var model in Document.Components)
        {
            var qualifiedName = model.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
            if (!qualifiedName.Contains('.')) qualifiedName = "System.Windows.Forms." + qualifiedName;
            // SerialPort is the only supported component outside the Forms assembly.
            // Resolve that exact built-in type without loading anything named by the project.
            var type = qualifiedName.Equals(typeof(SerialPort).FullName, StringComparison.OrdinalIgnoreCase)
                ? typeof(SerialPort)
                : typeof(Forms.Control).Assembly.GetType(qualifiedName, false, true);
            if (type is null || !IsSupportedComponentType(type))
            {
                problems.Add(model.Name + ": " + model.TypeName);
                continue;
            }

            try
            {
                var instance = (IComponent)Activator.CreateInstance(type)!;
                if (instance is Forms.Control control) control.Name = model.Name;
                _components[model.Name] = instance;
                foreach (var (property, syntax) in model.PropertySyntax)
                {
                    // Runtime activation belongs to the user's application, never its preview.
                    if (IsInertPreviewProperty(instance, property))
                        continue;
                    var descriptor = TypeDescriptor.GetProperties(instance)[property];
                    if (descriptor is null || descriptor.IsReadOnly) continue;
                    try
                    {
                        var value = WinFormsDesignerValues.Evaluate(syntax, descriptor.PropertyType);
                        if (value is Font { IsSystemFont: false } font) _previewFonts.Add(font);
                        descriptor.SetValue(instance, value);
                    }
                    catch (Exception exception)
                    {
                        problems.Add(model.Name + "." + property + ": " + exception.GetBaseException().Message);
                    }
                }
            }
            catch (Exception exception)
            {
                problems.Add(model.Name + ": " + exception.GetBaseException().Message);
            }
        }
    }

    internal IComponent[] TakeComponentPreviewsForDisposal()
    {
        var instances = _components.Values.ToArray();
        _components.Clear();
        return instances;
    }

    internal static void DisposeComponentPreviews(IEnumerable<IComponent> instances)
    {
        foreach (var instance in instances) instance.Dispose();
    }

    private WinFormsDesignerComponent ComponentModel(IComponent component) =>
        Document.Components.Single(model => model.Name.Equals(NameOfComponent(component), StringComparison.OrdinalIgnoreCase));

    private static bool IsInertPreviewProperty(IComponent component, string property) =>
        component is Forms.Timer && property.Equals(nameof(Forms.Timer.Enabled), StringComparison.OrdinalIgnoreCase)
        || component is Forms.ContextMenuStrip && property.Equals(nameof(Forms.ContextMenuStrip.Visible), StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedComponentType(Type type) =>
        type == typeof(Forms.Timer) || type == typeof(Forms.ImageList)
        || type == typeof(Forms.ToolTip) || type == typeof(Forms.ContextMenuStrip)
        || type == typeof(Forms.OpenFileDialog) || type == typeof(Forms.SaveFileDialog)
        || type == typeof(Forms.FolderBrowserDialog) || type == typeof(Forms.ColorDialog)
        || type == typeof(Forms.FontDialog) || type == typeof(SerialPort);
}
