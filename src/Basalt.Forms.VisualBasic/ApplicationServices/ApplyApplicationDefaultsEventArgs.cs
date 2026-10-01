namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// Carries the defaults a VB application framework applies before the main
/// form is created: font, high-DPI mode, color mode and how long the splash
/// screen is held on screen at minimum.
/// </summary>
public class ApplyApplicationDefaultsEventArgs : EventArgs
{
    // Only WindowsFormsApplicationBase creates one, as in Microsoft's assembly, which declares no public constructor.
    internal ApplyApplicationDefaultsEventArgs()
    {
    }

    public System.Windows.Forms.SystemColorMode ColorMode { get; set; }

    public System.Drawing.Font? Font { get; set; }

    public System.Windows.Forms.HighDpiMode HighDpiMode { get; set; }

    public int MinimumSplashScreenDisplayTime { get; set; }
}
