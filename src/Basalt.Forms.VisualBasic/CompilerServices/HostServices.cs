namespace Microsoft.VisualBasic.CompilerServices;

/// <summary>Holds the one IVbHost a VB application's host (the IDE, when debugging under it) installs; null when running stand-alone.</summary>
public sealed class HostServices
{
    private static IVbHost? s_vbHost;

    public HostServices()
    {
    }

    public static IVbHost? VBHost
    {
        get => s_vbHost;
        set => s_vbHost = value;
    }
}
