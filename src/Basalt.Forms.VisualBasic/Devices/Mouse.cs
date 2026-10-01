using System.Windows.Forms;

namespace Microsoft.VisualBasic.Devices;

/// <summary>The mouse, as My.Computer.Mouse exposes it to a VB application.</summary>
public class Mouse
{
    // Basalt.Forms has no setting for a swapped primary button; every mouse it
    // reports is right-handed, the common default.
    public bool ButtonsSwapped => false;

    public bool WheelExists => true;

    public int WheelScrollLines => SystemInformation.MouseWheelScrollLines;
}
