namespace System.Windows.Forms;

/// <summary>Whether a named feature exists on the running platform, and since which version.</summary>
public interface IFeatureSupport
{
    Version? GetVersionPresent(object feature);

    bool IsPresent(object feature);

    bool IsPresent(object feature, Version minimumVersion);
}
