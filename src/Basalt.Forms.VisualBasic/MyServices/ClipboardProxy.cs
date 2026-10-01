using System.Windows.Forms;

namespace Microsoft.VisualBasic.MyServices;

/// <summary>
/// My.Computer.Clipboard: an instance forward to the static System.Windows.Forms.Clipboard.
/// </summary>
/// <remarks>
/// Basalt.Forms's Clipboard only carries plain text today (System.Windows.Forms.Clipboard
/// has no SetImage, SetDataObject, file-drop list or TextDataFormat overloads yet),
/// so only the text members are implemented; the rest stay out rather than guess
/// at a shape Clipboard itself cannot back.
/// </remarks>
public class ClipboardProxy
{
    /// <summary>Created by My.Computer, not by applications: Microsoft's constructor is not public.</summary>
    internal ClipboardProxy()
    {
    }

    public void Clear() => Clipboard.Clear();

    public bool ContainsText() => Clipboard.ContainsText();

    public string GetText() => Clipboard.GetText();

    public void SetText(string text) => Clipboard.SetText(text);
}
