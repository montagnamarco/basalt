using System.Windows.Forms;

namespace Microsoft.VisualBasic.Devices;

/// <summary>The keyboard, as My.Computer.Keyboard exposes it to a VB application.</summary>
public class Keyboard
{
    public bool ShiftKeyDown => (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

    public bool AltKeyDown => (Control.ModifierKeys & Keys.Alt) == Keys.Alt;

    public bool CtrlKeyDown => (Control.ModifierKeys & Keys.Control) == Keys.Control;

    // The lock state as Control.IsKeyLocked follows it from the presses Basalt sees.
    public bool CapsLock => Control.IsKeyLocked(Keys.CapsLock);

    public bool NumLock => Control.IsKeyLocked(Keys.NumLock);

    public bool ScrollLock => Control.IsKeyLocked(Keys.Scroll);

    /// <summary>SendKeys has no synthesized-input backend in Basalt.Forms yet.</summary>
    public void SendKeys(string keys) => throw new PlatformNotSupportedException(
        "Keyboard.SendKeys is not supported: Basalt.Forms has no API to synthesize native key events.");

    /// <summary>SendKeys has no synthesized-input backend in Basalt.Forms yet.</summary>
    public void SendKeys(string keys, bool wait) => throw new PlatformNotSupportedException(
        "Keyboard.SendKeys is not supported: Basalt.Forms has no API to synthesize native key events.");
}
