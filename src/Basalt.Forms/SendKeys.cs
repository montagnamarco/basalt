using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Sends keystrokes the way typing them would: to the focused control of the
/// active form, through the same key messages <see
/// cref="Internal.Hosting.FormsInput"/> builds for a real key (so KeyDown,
/// KeyPress, KeyUp, shortcuts and dialog keys all see them), not a shortcut
/// that only fires an event.
/// </summary>
public class SendKeys
{
    private SendKeys()
    {
    }

    /// <summary>Sends the keys and returns; nothing here is actually queued, so they have already been delivered.</summary>
    public static void Send(string keys) => SendKeysParser.Parse(keys);

    /// <summary>Sends the keys, then processes whatever they caused (a posted continuation, a timer) before returning.</summary>
    public static void SendWait(string keys)
    {
        Send(keys);
        Flush();
    }

    /// <summary>Processes pending work, as if waiting for the keys sent so far to finish being handled.</summary>
    public static void Flush() => Application.DoEvents();
}
