using System.Linq;
using System.Reflection;

namespace Basalt.Core.Settings;

/// <summary>
/// What has been changed from how it comes out of the box.
///
/// Worked out by reading the properties rather than by each control saying so:
/// a setting added later is then covered without anyone remembering to.
/// </summary>
public static class SettingsComparison
{
    /// <summary>The settings groups, by the name their property carries.</summary>
    private static readonly Func<IdeSettings, object>[] Groups =
    [
        s => s.Editor, s => s.Appearance, s => s.Terminal,
        s => s.Debug, s => s.Ai, s => s.Keyboard, s => s.Build
    ];

    /// <summary>
    /// The names of the settings in one group that differ from the default.
    ///
    /// The group is named as its property is: "Editor", "Appearance".
    /// </summary>
    public static IReadOnlyList<string> ChangedIn(IdeSettings settings, string groupName)
    {
        var defaults = new IdeSettings();

        var property = typeof(IdeSettings).GetProperty(groupName);

        if (property is null) return [];

        var mine = property.GetValue(settings);
        var theirs = property.GetValue(defaults);

        if (mine is null || theirs is null) return [];

        return [.. Differences(mine, theirs)];
    }

    /// <summary>Whether anything in a group differs from the default.</summary>
    public static bool HasChangesIn(IdeSettings settings, string groupName) =>
        ChangedIn(settings, groupName).Count > 0;

    /// <summary>
    /// Puts one group back to how it comes out of the box.
    ///
    /// The whole group rather than one setting: the window offers the reset
    /// per page, which is the unit a user thinks in.
    /// </summary>
    public static void ResetGroup(IdeSettings settings, string groupName)
    {
        var property = typeof(IdeSettings).GetProperty(groupName);

        if (property is null || !property.CanWrite) return;

        var fresh = new IdeSettings();

        property.SetValue(settings, property.GetValue(fresh));
    }

    /// <summary>Every settings group that differs from the default.</summary>
    public static IReadOnlyList<string> ChangedGroups(IdeSettings settings) =>
    [
        .. typeof(IdeSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string))
            .Where(p => !p.PropertyType.IsGenericType)
            .Select(p => p.Name)
            .Where(name => HasChangesIn(settings, name))
    ];

    /// <summary>
    /// Whether two values are the same setting.
    ///
    /// A collection is compared by what it holds: two empty dictionaries are
    /// not Equals to each other, and comparing them by reference would report
    /// every fresh settings file as having been changed.
    /// </summary>
    private static bool Same(object? a, object? b)
    {
        if (Equals(a, b)) return true;
        if (a is null || b is null) return false;

        if (a is System.Collections.IEnumerable first
            && b is System.Collections.IEnumerable second)
        {
            return first.Cast<object?>().SequenceEqual(second.Cast<object?>());
        }

        return false;
    }

    /// <summary>The properties of two objects of one type that differ.</summary>
    private static IEnumerable<string> Differences(object mine, object theirs)
    {
        foreach (var property in mine.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead) continue;

            var a = property.GetValue(mine);
            var b = property.GetValue(theirs);

            if (!Same(a, b)) yield return property.Name;
        }
    }
}
