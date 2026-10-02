using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>The keyboard layout is about to change; cancelling keeps the current one.</summary>
public class InputLanguageChangingEventArgs : CancelEventArgs
{
    public InputLanguageChangingEventArgs(CultureInfo culture, bool sysCharSet)
        : this(InputLanguage.FromCulture(culture), sysCharSet)
    {
    }

    public InputLanguageChangingEventArgs(InputLanguage inputLanguage, bool sysCharSet)
    {
        InputLanguage = inputLanguage;
        Culture = inputLanguage.Culture;
        SysCharSet = sysCharSet;
    }

    public CultureInfo Culture { get; }

    public InputLanguage InputLanguage { get; }

    public bool SysCharSet { get; }
}
