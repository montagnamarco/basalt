using System.Reflection;

namespace System.Windows.Forms;

/// <summary>
/// Base for a set of named features and the version each has been present
/// since, found the way WinForms always did: a static property, by
/// reflection, on a type named by the caller.
/// </summary>
public abstract class FeatureSupport : IFeatureSupport
{
    protected FeatureSupport()
    {
    }

    public abstract Version? GetVersionPresent(object feature);

    public virtual bool IsPresent(object feature) => GetVersionPresent(feature) is not null;

    public virtual bool IsPresent(object feature, Version minimumVersion)
    {
        ArgumentNullException.ThrowIfNull(minimumVersion);

        return GetVersionPresent(feature) is { } version && version >= minimumVersion;
    }

    /// <summary>The version a static property named <paramref name="staticPropertyName"/> on the type named <paramref name="typeName"/> answers, or null when either does not exist.</summary>
    public static Version? GetVersionPresent(string typeName, string staticPropertyName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(staticPropertyName);

        var type = Type.GetType(typeName);
        var property = type?.GetProperty(staticPropertyName, BindingFlags.Public | BindingFlags.Static);

        return property?.GetValue(null, null) as Version;
    }

    public static bool IsPresent(string typeName, string staticPropertyName) => GetVersionPresent(typeName, staticPropertyName) is not null;

    public static bool IsPresent(string typeName, string staticPropertyName, Version minimumVersion)
    {
        ArgumentNullException.ThrowIfNull(minimumVersion);

        return GetVersionPresent(typeName, staticPropertyName) is { } version && version >= minimumVersion;
    }
}
