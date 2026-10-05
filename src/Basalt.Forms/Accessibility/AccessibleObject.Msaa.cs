using Accessibility;

namespace System.Windows.Forms;

public partial class AccessibleObject : IAccessible
{
    object? IAccessible.accParent => Parent;

    int IAccessible.accChildCount => Math.Max(0, GetChildCount());

    object? IAccessible.get_accChild(object childId) => ChildForId(childId, allowSelf: false);

    string? IAccessible.get_accName(object childId) => ChildForId(childId)?.Name;

    void IAccessible.set_accName(object childId, string newName)
    {
        if (ChildForId(childId) is { } child) child.Name = newName;
    }

    string? IAccessible.get_accValue(object childId) => ChildForId(childId)?.Value;

    void IAccessible.set_accValue(object childId, string newValue)
    {
        if (ChildForId(childId) is { } child) child.Value = newValue;
    }

    string? IAccessible.get_accDescription(object childId) => ChildForId(childId)?.Description;

    object? IAccessible.get_accRole(object childId) => (int)(ChildForId(childId)?.Role ?? AccessibleRole.None);

    object? IAccessible.get_accState(object childId) => (int)(ChildForId(childId)?.State ?? AccessibleStates.None);

    string? IAccessible.get_accHelp(object childId) => ChildForId(childId)?.Help;

    int IAccessible.get_accHelpTopic(out string helpFile, object childId)
    {
        string? fileName = null;
        var child = ChildForId(childId);
        var result = child is null ? -1 : child.GetHelpTopic(out fileName);
        helpFile = fileName ?? string.Empty;
        return result;
    }

    string? IAccessible.get_accKeyboardShortcut(object childId) => ChildForId(childId)?.KeyboardShortcut;

    object? IAccessible.accFocus => GetFocused();

    object? IAccessible.accSelection => GetSelected();

    string? IAccessible.get_accDefaultAction(object childId) => ChildForId(childId)?.DefaultAction;

    void IAccessible.accSelect(int flagsSelect, object childId) =>
        ChildForId(childId)?.Select((AccessibleSelection)flagsSelect);

    void IAccessible.accLocation(out int x, out int y, out int width, out int height, object childId)
    {
        var bounds = ChildForId(childId)?.Bounds ?? Drawing.Rectangle.Empty;
        x = bounds.X;
        y = bounds.Y;
        width = bounds.Width;
        height = bounds.Height;
    }

    object? IAccessible.accNavigate(int direction, object start) =>
        ChildForId(start)?.Navigate((AccessibleNavigation)direction);

    object? IAccessible.accHitTest(int x, int y) => HitTest(x, y);

    void IAccessible.accDoDefaultAction(object childId) => ChildForId(childId)?.DoDefaultAction();

    private AccessibleObject? ChildForId(object? childId, bool allowSelf = true)
    {
        if (childId is null or 0) return allowSelf ? this : null;
        if (childId is int index && index > 0 && index <= GetChildCount()) return GetChild(index - 1);
        return null;
    }
}
