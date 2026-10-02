using System.Globalization;

namespace System.Windows.Forms;

/// <summary>The keyboard layout changed; CharSet is the Win32 character set of the new layout's code page.</summary>
public class InputLanguageChangedEventArgs : EventArgs
{
    public InputLanguageChangedEventArgs(CultureInfo culture, byte charSet)
        : this(InputLanguage.FromCulture(culture), charSet)
    {
    }

    public InputLanguageChangedEventArgs(InputLanguage inputLanguage, byte charSet)
    {
        InputLanguage = inputLanguage;
        Culture = inputLanguage.Culture;
        CharSet = charSet;
    }

    public CultureInfo Culture { get; }

    public InputLanguage InputLanguage { get; }

    public byte CharSet { get; }
}
