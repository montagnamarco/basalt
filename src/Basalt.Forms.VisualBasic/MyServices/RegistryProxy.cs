using Microsoft.Win32;

namespace Microsoft.VisualBasic.MyServices;

/// <summary>
/// My.Computer.Registry: the Windows registry's root keys, as an instance
/// forward to Microsoft.Win32.Registry.
/// </summary>
/// <remarks>
/// The registry is a Windows-only concept with no portable analogue (not even
/// a reasonable emulation on Linux or macOS): every member throws
/// PlatformNotSupportedException off Windows, checked explicitly here rather
/// than left to whatever Microsoft.Win32.Registry happens to do on another OS
/// (it returns null from the root properties instead of throwing). The check
/// is inlined in every member (rather than shared through a helper method) so
/// the platform-compatibility analyzer recognizes it as a guard and does not
/// flag the Windows-only call beneath it.
/// </remarks>
public class RegistryProxy
{
    /// <summary>Created by My.Computer, not by applications: Microsoft's constructor is not public.</summary>
    internal RegistryProxy()
    {
    }

    private const string RegistryUnsupported = "The Windows registry is not available on this operating system.";

    public RegistryKey ClassesRoot
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.ClassesRoot;
        }
    }

    public RegistryKey CurrentConfig
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.CurrentConfig;
        }
    }

    public RegistryKey CurrentUser
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.CurrentUser;
        }
    }

    public RegistryKey LocalMachine
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.LocalMachine;
        }
    }

    public RegistryKey PerformanceData
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.PerformanceData;
        }
    }

    public RegistryKey Users
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);
            return Registry.Users;
        }
    }

    public object GetValue(string keyName, string valueName, object defaultValue)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);

        return Registry.GetValue(keyName, valueName, defaultValue) ?? defaultValue;
    }

    public void SetValue(string keyName, string valueName, object value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);

        Registry.SetValue(keyName, valueName, value);
    }

    public void SetValue(string keyName, string valueName, object value, RegistryValueKind valueKind)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(RegistryUnsupported);

        Registry.SetValue(keyName, valueName, value, valueKind);
    }
}
