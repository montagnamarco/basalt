using System.ComponentModel;

namespace Basalt.Designer;

/// <summary>Event metadata shared by Forms controls and component-model custom controls.</summary>
public sealed record WinFormsDesignableEvent(string Name, Type EventType, string Category,
    string? Description, string? HandlerName, bool IsDefault);

public static class WinFormsEventInspector
{
    public static IReadOnlyList<WinFormsDesignableEvent> Describe(object component,
        Func<string, string?>? existingHandler = null) =>
        Describe(TypeDescriptor.GetEvents(component), TypeDescriptor.GetDefaultEvent(component)?.Name, existingHandler);

    public static IReadOnlyList<WinFormsDesignableEvent> Describe(Type componentType,
        Func<string, string?>? existingHandler = null) =>
        Describe(TypeDescriptor.GetEvents(componentType), TypeDescriptor.GetDefaultEvent(componentType)?.Name, existingHandler);

    private static IReadOnlyList<WinFormsDesignableEvent> Describe(EventDescriptorCollection descriptors,
        string? defaultEventName, Func<string, string?>? existingHandler) =>
        descriptors.Cast<EventDescriptor>()
            .Where(descriptor => descriptor.IsBrowsable && typeof(Delegate).IsAssignableFrom(descriptor.EventType))
            .Select(descriptor => new WinFormsDesignableEvent(descriptor.Name, descriptor.EventType,
                descriptor.Category, string.IsNullOrEmpty(descriptor.Description) ? null : descriptor.Description,
                existingHandler?.Invoke(descriptor.Name), descriptor.Name == defaultEventName))
            .OrderByDescending(descriptor => descriptor.IsDefault)
            .ThenBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
