using System.ComponentModel;
using System.Reflection;

namespace Basalt.Designer.Toolbox;

public sealed record WinFormsToolboxItem(Type ControlType)
{
    public string DisplayName => ControlType.Name;
    public override string ToString() => DisplayName;
}

/// <summary>Discovers usable controls from the supplied Forms or custom-control assembly.</summary>
public static class WinFormsToolboxCatalog
{
    public static IReadOnlyList<WinFormsToolboxItem> Describe(Assembly assembly, Type controlBaseType) =>
        SafeTypes(assembly)
            .Where(type => type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters &&
                type != controlBaseType && controlBaseType.IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .Where(type => TypeDescriptor.GetAttributes(type)[typeof(ToolboxItemAttribute)] is not ToolboxItemAttribute attribute ||
                !attribute.Equals(ToolboxItemAttribute.None))
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Select(type => new WinFormsToolboxItem(type)).ToArray();

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
