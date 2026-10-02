using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// A keyboard layout. Windows can have several installed and switches among
/// them; this platform has no keyboard layout API of its own, so the single
/// language reported everywhere is the thread's current culture, and
/// changing it only changes what WinForms itself remembers, not real input.
/// </summary>
public sealed class InputLanguage
{
    private static InputLanguage? s_current;

    private InputLanguage(CultureInfo culture)
    {
        Culture = culture;
    }

    public CultureInfo Culture { get; }

    /// <summary>The Win32 keyboard layout handle (HKL) a real one would have: synthesised from the LCID, since none exists here.</summary>
    public IntPtr Handle => (IntPtr)Culture.LCID;

    /// <summary>The name Windows shows for the layout; approximated by the culture's own display name.</summary>
    public string LayoutName => Culture.DisplayName;

    /// <summary>
    /// The language input is reported in. Setting it does not change any real
    /// keyboard layout (there is none to change); it only changes what this
    /// property, and the <see cref="Application.CurrentInputLanguage"/> it
    /// backs, answer afterwards.
    /// </summary>
    public static InputLanguage CurrentInputLanguage
    {
        get => s_current ??= FromCulture(CultureInfo.CurrentCulture);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            s_current = value;
        }
    }

    /// <summary>The culture the process started with: the only layout this platform ever installs.</summary>
    public static InputLanguage DefaultInputLanguage { get; } = FromCulture(CultureInfo.InstalledUICulture);

    /// <summary>Always the current input language alone: there is no second layout to switch to.</summary>
    public static InputLanguageCollection InstalledInputLanguages => new([CurrentInputLanguage]);

    public static InputLanguage FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new InputLanguage(culture);
    }

    public override bool Equals(object? value) => value is InputLanguage other && other.Culture.LCID == Culture.LCID;

    public override int GetHashCode() => Culture.LCID;
}
