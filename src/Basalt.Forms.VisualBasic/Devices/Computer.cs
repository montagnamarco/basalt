using System.Windows.Forms;
using Microsoft.VisualBasic.MyServices;

namespace Microsoft.VisualBasic.Devices;

/// <summary>The desktop machine My.Computer exposes to a VB application: ServerComputer plus the input and display devices a service has no use for.</summary>
public class Computer : ServerComputer
{
    public Audio Audio { get; } = new();

    public ClipboardProxy Clipboard { get; } = new();

    public Keyboard Keyboard { get; } = new();

    public Mouse Mouse { get; } = new();

    public Screen Screen => Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen is available.");
}
