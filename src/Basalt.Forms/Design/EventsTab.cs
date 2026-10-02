using System.ComponentModel;

namespace System.Windows.Forms.Design;

/// <summary>
/// The tab a PropertyGrid shows when its "events" toolbar button is pressed:
/// one row per event instead of one row per property, each holding the name
/// of the method it is wired to.
/// </summary>
/// <remarks>
/// Visual Studio's real EventsTab asks an IEventBindingService (from the
/// designer host) to generate a handler stub and wire it into the form's
/// code the moment a name is typed; Basalt has no designer host or code
/// generator here, so EventPropertyDescriptor only remembers the name it was
/// given (per component, for the process's lifetime) instead of writing any
/// code - a PropertyGrid bound to EventsTab still lists every event and lets
/// a caller read back whatever name it set, which is what GetProperties's
/// contract promises; it does not make a running form actually call a method
/// by that name.
/// </remarks>
public class EventsTab : PropertyTab
{
    private readonly IServiceProvider? _serviceProvider;

    public EventsTab(IServiceProvider? sp)
    {
        _serviceProvider = sp;
    }

    public override string HelpKeyword => "net.ComponentModel.EventsTab";

    public override string TabName => "Events";

    public override bool CanExtend(object? extendee) => extendee is not null && TypeDescriptor.GetEvents(extendee).Count > 0;

    public override PropertyDescriptor? GetDefaultProperty(object component)
    {
        var defaultEvent = TypeDescriptor.GetDefaultEvent(component);
        return defaultEvent is null ? null : new EventPropertyDescriptor(defaultEvent);
    }

    public override PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes)
    {
        var events = TypeDescriptor.GetEvents(component, attributes!);
        var properties = new PropertyDescriptor[events.Count];

        for (var i = 0; i < events.Count; i++) properties[i] = new EventPropertyDescriptor(events[i]!);

        return new PropertyDescriptorCollection(properties);
    }

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object component, Attribute[]? attributes) =>
        GetProperties(component, attributes);

    /// <summary>One event, shown as a string property: its value is the handler method name last assigned through this descriptor (see the type's remarks for why that is as far as it goes).</summary>
    private sealed class EventPropertyDescriptor(EventDescriptor eventDescriptor) : PropertyDescriptor(eventDescriptor.Name, (Attribute[])[.. eventDescriptor.Attributes.Cast<Attribute>()])
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, string?>> s_handlerNames = new();

        public override Type ComponentType => eventDescriptor.ComponentType;

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component) => GetHandlerNames(component).ContainsKey(eventDescriptor.Name);

        public override object? GetValue(object? component) =>
            component is not null && GetHandlerNames(component).TryGetValue(eventDescriptor.Name, out var name) ? name : null;

        public override void ResetValue(object component) => GetHandlerNames(component).Remove(eventDescriptor.Name);

        public override void SetValue(object? component, object? value)
        {
            if (component is null) return;

            GetHandlerNames(component)[eventDescriptor.Name] = (string?)value;
            OnValueChanged(component, EventArgs.Empty);
        }

        public override bool ShouldSerializeValue(object component) => CanResetValue(component);

        private static Dictionary<string, string?> GetHandlerNames(object component) =>
            s_handlerNames.GetOrCreateValue(component);
    }
}
