using System.ComponentModel;
using System.Reflection;

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

        if (!IsTrustedDesignerComponent(component, descriptor))
        {
            // TypeDescriptor hooks on application classes can execute arbitrary code.
            // Preserve the source assignment when its metadata is not trusted.
            return new(true, true);
        }

        try
        {
            return DescribeTrusted(component, descriptor, sourceValue, resourceCanReset);
        }
        catch (Exception) when (ShouldContainHookFailure(component, descriptor))
        {
            // A user subclass's own getter, ShouldSerializeX or CanResetX threw. The row
            // must still render, so the designer reports the property as modified (its
            // source assignment is preserved) and not resettable, rather than failing.
            // A failure in a built-in Forms or SerialPort hook is not user code and is not
            // caught here: the filter lets it propagate, so Basalt's own bugs stay visible.
            return new(true, false);
        }
    }

    private static WinFormsPropertyResetState DescribeTrusted(
        object component,
        PropertyDescriptor descriptor,
        WinFormsSourceValueState sourceValue,
        bool resourceCanReset)
    {
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

    /// <summary>
    /// Whether the component's type and the property's declaring type both descend from an
    /// assembly the designer already trusts: System.Windows.Forms (the reimplemented Basalt.Forms
    /// every built-in control comes from) and System.IO.Ports (SerialPort; see
    /// WinFormsDesignerSession.IsSupportedComponentType). Each base chain is walked, so a user
    /// control - a subclass of a Control or a Component, declared in the user's own assembly -
    /// is trusted too.
    /// <para>
    /// That trust covers the user subclass's own hooks, not only the members it inherits: its
    /// own property getters, ShouldSerializeX and ResetX methods run during metadata inspection.
    /// This is a deliberate owner decision, the same one Visual Studio makes: user controls run
    /// in-process. They already run that code in the same process through their constructor and
    /// OnPaint, so reading their property metadata adds no new trust boundary.
    /// </para>
    /// <para>
    /// Exceptions thrown by a user subclass's hooks are contained by <see cref="Describe"/>, which reports the
    /// property as modified and not resettable, and <see cref="Basalt.Designer.WinFormsPropertyInspector.ResetValue"/>,
    /// which reports it as not reset. An exception from a hook declared on a built-in Forms or SerialPort
    /// type is not contained: it propagates, because it is a bug in Basalt's own controls and must stay
    /// visible (see <see cref="ShouldContainHookFailure"/>). A type with no such ancestor - a genuinely
    /// unrelated application type - stays untrusted, and its hooks never run.
    /// </para>
    /// </summary>
    internal static bool IsTrustedDesignerComponent(object component, PropertyDescriptor descriptor) =>
        DerivesFromTrustedAssembly(component.GetType()) && DerivesFromTrustedAssembly(descriptor.ComponentType);

    /// <summary>
    /// Whether an exception from the component's own hooks is contained as user code's failure. Only a
    /// trusted component (see <see cref="IsTrustedDesignerComponent"/>) whose own type, or whose property's
    /// declaring type, is declared outside System.Windows.Forms and System.IO.Ports qualifies: that is a user
    /// subclass. A component and property both from the built-in assemblies do not qualify, so their
    /// exceptions propagate.
    /// </summary>
    internal static bool ShouldContainHookFailure(object component, PropertyDescriptor descriptor) =>
        IsTrustedDesignerComponent(component, descriptor) &&
        (!IsTrustedAssembly(component.GetType().Assembly) || !IsTrustedAssembly(descriptor.ComponentType.Assembly));

    private static bool DerivesFromTrustedAssembly(Type? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsTrustedAssembly(current.Assembly)) return true;
        }

        return false;
    }

    private static bool IsTrustedAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is "System.Windows.Forms" or "System.IO.Ports";
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
