namespace Microsoft.VisualBasic.Devices;

/// <summary>Carries the network's new reachability to Network.NetworkAvailabilityChanged.</summary>
public class NetworkAvailableEventArgs(bool networkAvailable) : EventArgs
{
    public bool IsNetworkAvailable { get; } = networkAvailable;
}
