using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace System.Windows.Forms;

/// <summary>
/// What an assistive technology asks about one part of the user interface:
/// its place on screen, its name, role and state, and its children.
/// </summary>
/// <remarks>
/// A plain instance answers with WinForms' own defaults: nothing is known
/// about it. <see cref="Control.ControlAccessibleObject"/> answers for a
/// control instead, and does it through the single <see cref="OwnerControl"/>
/// hook below rather than overriding every member here: that matches
/// Microsoft's own public surface, where ControlAccessibleObject overrides
/// only Name, Role, Parent and a handful of others that are not already
/// driven by the owning control's own state.
/// </remarks>
public partial class AccessibleObject : StandardOleMarshalObject, IReflect
{
    public AccessibleObject()
    {
    }

    /// <summary>
    /// The control this object speaks for, or null for one created on its
    /// own. Not part of the public contract (Microsoft has no such member):
    /// it only lets the defaults below read bounds, state and children from
    /// a control without <see cref="Control.ControlAccessibleObject"/> having
    /// to override each of them.
    /// </summary>
    internal virtual Control? OwnerControl => null;

    public virtual Rectangle Bounds => OwnerControl is { IsHandleCreated: true } owner
        ? owner.RectangleToScreen(new Rectangle(Point.Empty, owner.Size))
        : Rectangle.Empty;

    public virtual string? DefaultAction => null;

    public virtual string? Description => null;

    public virtual string? Help => null;

    public virtual string? KeyboardShortcut => null;

    public virtual string? Name { get; set; }

    public virtual AccessibleObject? Parent => null;

    public virtual AccessibleRole Role => AccessibleRole.None;

    public virtual AccessibleStates State
    {
        get
        {
            if (OwnerControl is not { } owner) return AccessibleStates.None;

            var state = AccessibleStates.None;
            if (!owner.Enabled) state |= AccessibleStates.Unavailable;
            if (!owner.Visible) state |= AccessibleStates.Invisible;
            if (owner.CanFocus) state |= AccessibleStates.Focusable;
            if (owner.Focused) state |= AccessibleStates.Focused;

            return state;
        }
    }

    public virtual string? Value { get; set; }

    public virtual void DoDefaultAction()
    {
        // Nothing to do without a platform bridge: a real default action
        // (clicking a button, checking a box) belongs to whichever Control
        // subclass later overrides CreateAccessibilityInstance for itself.
    }

    public virtual AccessibleObject GetChild(int index)
    {
        var children = VisibleChildren();

        if (children is not null && index >= 0 && index < children.Length) return children[index].AccessibilityObject;

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public virtual int GetChildCount() => VisibleChildren()?.Length ?? -1;

    public virtual AccessibleObject GetFocused()
    {
        foreach (var child in VisibleChildren() ?? [])
        {
            if (child.Focused) return child.AccessibilityObject;
        }

        // A leaf (or a parent none of whose children has it) answers for itself.
        // Microsoft's own signature is not annotated nullable here either,
        // although returning "nothing found" is exactly what WinForms does.
        return State.HasFlag(AccessibleStates.Focused) ? this : null!;
    }

    public virtual int GetHelpTopic(out string? fileName)
    {
        fileName = null;
        return -1;
    }

    public virtual AccessibleObject GetSelected()
    {
        foreach (var child in VisibleChildren() ?? [])
        {
            if (child.AccessibilityObject.State.HasFlag(AccessibleStates.Selected)) return child.AccessibilityObject;
        }

        return State.HasFlag(AccessibleStates.Selected) ? this : null!;
    }

    public virtual AccessibleObject HitTest(int x, int y)
    {
        foreach (var child in VisibleChildren() ?? [])
        {
            if (child.IsHandleCreated && child.RectangleToScreen(new Rectangle(Point.Empty, child.Size)).Contains(x, y))
            {
                return child.AccessibilityObject;
            }
        }

        return null!;
    }

    public virtual AccessibleObject Navigate(AccessibleNavigation navdir)
    {
        var children = VisibleChildren();

        return navdir switch
        {
            AccessibleNavigation.FirstChild when children is { Length: > 0 } => children[0].AccessibilityObject,
            AccessibleNavigation.LastChild when children is { Length: > 0 } => children[^1].AccessibilityObject,
            _ => null!,
        };
    }

    public virtual bool RaiseLiveRegionChanged() => false;

    public bool RaiseAutomationNotification(
        Automation.AutomationNotificationKind notificationKind,
        Automation.AutomationNotificationProcessing notificationProcessing,
        string notificationText)
    {
        ArgumentNullException.ThrowIfNull(notificationText);

        // Avalonia exposes control peers, but has no notification event in its
        // automation provider contract. False reports that nothing was raised.
        return false;
    }

    public virtual void Select(AccessibleSelection flags)
    {
        if (OwnerControl is not { } owner) return;

        if (flags.HasFlag(AccessibleSelection.TakeFocus)) owner.Focus();
        if (flags.HasFlag(AccessibleSelection.TakeSelection)) owner.Select();
    }

    /// <summary>
    /// Historically wraps the handle's native accessible object so the rest
    /// of this class can forward to it. We have none to wrap: the Control
    /// hook above already answers Bounds, State and the children, so this
    /// stays a documented no-op kept callable for subclasses.
    /// </summary>
    protected void UseStdAccessibleObjects(IntPtr handle)
    {
    }

    protected void UseStdAccessibleObjects(IntPtr handle, int objid)
    {
    }

    private Control[]? VisibleChildren()
    {
        if (OwnerControl is not { } owner) return null;

        var children = new List<Control>(owner.Controls.Count);
        for (var i = 0; i < owner.Controls.Count; i++)
        {
            if (owner.Controls[i].Visible) children.Add(owner.Controls[i]);
        }

        return [.. children];
    }

    // System.Reflection.IReflect: a COM customer can ask for this object's
    // own members the same way it would ask a .NET object through late
    // binding. Nothing here changes what reflection would already answer;
    // it only forwards to the runtime type, explicitly so it stays off
    // IntelliSense as it does on Microsoft's own AccessibleObject.

    FieldInfo? IReflect.GetField(string name, BindingFlags bindingAttr) => GetType().GetField(name, bindingAttr);

    FieldInfo[] IReflect.GetFields(BindingFlags bindingAttr) => GetType().GetFields(bindingAttr);

    MemberInfo[] IReflect.GetMember(string name, BindingFlags bindingAttr) => GetType().GetMember(name, bindingAttr);

    MemberInfo[] IReflect.GetMembers(BindingFlags bindingAttr) => GetType().GetMembers(bindingAttr);

    MethodInfo? IReflect.GetMethod(string name, BindingFlags bindingAttr) => GetType().GetMethod(name, bindingAttr);

    MethodInfo? IReflect.GetMethod(string name, BindingFlags bindingAttr, Binder? binder, Type[] types, ParameterModifier[]? modifiers) =>
        GetType().GetMethod(name, bindingAttr, binder, types, modifiers);

    MethodInfo[] IReflect.GetMethods(BindingFlags bindingAttr) => GetType().GetMethods(bindingAttr);

    PropertyInfo? IReflect.GetProperty(string name, BindingFlags bindingAttr) => GetType().GetProperty(name, bindingAttr);

    PropertyInfo? IReflect.GetProperty(string name, BindingFlags bindingAttr, Binder? binder, Type? returnType, Type[] types, ParameterModifier[]? modifiers) =>
        GetType().GetProperty(name, bindingAttr, binder, returnType, types, modifiers);

    PropertyInfo[] IReflect.GetProperties(BindingFlags bindingAttr) => GetType().GetProperties(bindingAttr);

    object? IReflect.InvokeMember(
        string name,
        BindingFlags invokeAttr,
        Binder? binder,
        object? target,
        object?[]? args,
        ParameterModifier[]? modifiers,
        Globalization.CultureInfo? culture,
        string[]? namedParameters) =>
        GetType().InvokeMember(name, invokeAttr, binder, target, args, modifiers, culture, namedParameters);

    Type IReflect.UnderlyingSystemType => GetType();
}
