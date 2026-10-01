using Microsoft.VisualBasic.MyServices;

namespace Microsoft.VisualBasic.Devices;

/// <summary>
/// A machine, with no need for the desktop-only members Computer adds: the
/// server-side half of My.Computer.
/// </summary>
public class ServerComputer
{
    public string Name => Environment.MachineName;

    public Clock Clock { get; } = new();

    public FileSystemProxy FileSystem { get; } = new();

    public ComputerInfo Info { get; } = new();

    public Network Network { get; } = new();

    public RegistryProxy Registry { get; } = new();
}
