using System.ComponentModel;

namespace Basalt.Designer;

/// <summary>Whether a source assignment was applied to the safe designer preview.</summary>
public enum WinFormsSourceValueState
{
    Absent,
    Evaluated,
    Unresolved,
    ResourceBacked,
    ResourceBackedUnresolved,
}

/// <summary>Visual Studio-style modification and reset state for a property row.</summary>
public readonly record struct WinFormsPropertyResetState(bool IsModified, bool CanReset);

public static class WinFormsPropertyResetMetadata
{
    /// <summary>
    /// Computes display state without evaluating source expressions or invoking application converters.
    /// The caller applies reset through an undoable designer or resource transaction.
    /// </summary>
    public static WinFormsPropertyResetState Describe(
        object component,
        PropertyDescriptor descriptor,
        WinFormsSourceValueState sourceValue,
        bool resourceCanReset = false)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (sourceValue == WinFormsSourceValueState.Absent)
        {
            return new(false, false);
        }

        if (descriptor.IsReadOnly)
        {
            return new(sourceValue is WinFormsSourceValueState.Unresolved
                or WinFormsSourceValueState.ResourceBackedUnresolved, false);
        }

        if (sourceValue == WinFormsSourceValueState.ResourceBackedUnresolved)
            return new(true, resourceCanReset);

        if (sourceValue == WinFormsSourceValueState.Unresolved)
        {
            // The preview kept its default because the source expression could not be
            // evaluated. Comparing preview values here would hide a real assignment.
            return new(true, true);
        }

        if (!IsBuiltInFormsComponent(component, descriptor))
        {
            // TypeDescriptor hooks on application classes can execute arbitrary code.
            // Preserve the source assignment when its metadata is not trusted.
            return new(true, true);
        }

        var defaultAttribute = descriptor.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;
        var value = descriptor.GetValue(component);
        bool modified;
        if (defaultAttribute is not null)
        {
            modified = !Equals(value, defaultAttribute.Value);
        }
        else if (IsControlText(component, descriptor) && value is string text)
        {
            // Basalt Forms still lacks Control.ShouldSerializeText. WinForms treats
            // empty Text as its default even though ReflectionPropertyDescriptor
            // otherwise reports every value as serializable without that hook.
            modified = text.Length != 0;
        }
        else
        {
            modified = descriptor.ShouldSerializeValue(component);
        }

        var canReset = modified && (sourceValue == WinFormsSourceValueState.ResourceBacked
            ? resourceCanReset : descriptor.CanResetValue(component));
        return new(modified, canReset);
    }

    private static bool IsBuiltInFormsComponent(object component, PropertyDescriptor descriptor)
    {
        const string formsAssembly = "System.Windows.Forms";
        return component.GetType().Assembly.GetName().Name == formsAssembly
            && descriptor.ComponentType.Assembly.GetName().Name == formsAssembly;
    }

    private static bool IsControlText(object component, PropertyDescriptor descriptor)
    {
        if (descriptor.Name != "Text" || descriptor.PropertyType != typeof(string))
        {
            return false;
        }

        for (var type = component.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == "System.Windows.Forms.Control")
            {
                return true;
            }
        }

        return false;
    }
}
