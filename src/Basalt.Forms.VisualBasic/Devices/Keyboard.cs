using System.Windows.Forms;

namespace Microsoft.VisualBasic.Devices;

/// <summary>The keyboard, as My.Computer.Keyboard exposes it to a VB application.</summary>
public class Keyboard
{
    public bool ShiftKeyDown => (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

    public bool AltKeyDown => (Control.ModifierKeys & Keys.Alt) == Keys.Alt;

    public bool CtrlKeyDown => (Control.ModifierKeys & Keys.Control) == Keys.Control;

    // Basalt.Forms does not track a toggle key's lock state (Control has no IsKeyLocked
    // yet): reporting "not locked" is the same answer Windows gives on a keyboard
    // without the key, so it is a safe default rather than a guess at the true state.
    public bool CapsLock => false;

    public bool NumLock => false;

    public bool ScrollLock => false;

    /// <summary>SendKeys has no synthesized-input backend in Basalt.Forms yet.</summary>
    public void SendKeys(string keys) => throw new PlatformNotSupportedException(
        "Keyboard.SendKeys is not supported: Basalt.Forms has no API to synthesize native key events.");

    /// <summary>SendKeys has no synthesized-input backend in Basalt.Forms yet.</summary>
    public void SendKeys(string keys, bool wait) => throw new PlatformNotSupportedException(
        "Keyboard.SendKeys is not supported: Basalt.Forms has no API to synthesize native key events.");
}
