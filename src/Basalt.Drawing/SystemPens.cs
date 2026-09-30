namespace System.Drawing;

/// <summary>A pen one pixel wide for each colour of the system's colour scheme.</summary>
/// <remarks>
/// The colours are the ones <see cref="SystemColors"/> answers with, which off
/// Windows is a fixed table in the framework: the default skin draws with the
/// same table, so a control painted with these matches the ones around it.
/// </remarks>
public static class SystemPens
{
    private static readonly Pen?[] Cache = new Pen?[(int)KnownColor.RebeccaPurple + 1];

    public static Pen ActiveBorder => FromSystemColor(SystemColors.ActiveBorder);
    public static Pen ActiveCaption => FromSystemColor(SystemColors.ActiveCaption);
    public static Pen ActiveCaptionText => FromSystemColor(SystemColors.ActiveCaptionText);
    public static Pen AppWorkspace => FromSystemColor(SystemColors.AppWorkspace);
    public static Pen ButtonFace => FromSystemColor(SystemColors.ButtonFace);
    public static Pen ButtonHighlight => FromSystemColor(SystemColors.ButtonHighlight);
    public static Pen ButtonShadow => FromSystemColor(SystemColors.ButtonShadow);
    public static Pen Control => FromSystemColor(SystemColors.Control);
    public static Pen ControlDark => FromSystemColor(SystemColors.ControlDark);
    public static Pen ControlDarkDark => FromSystemColor(SystemColors.ControlDarkDark);
    public static Pen ControlLight => FromSystemColor(SystemColors.ControlLight);
    public static Pen ControlLightLight => FromSystemColor(SystemColors.ControlLightLight);
    public static Pen ControlText => FromSystemColor(SystemColors.ControlText);
    public static Pen Desktop => FromSystemColor(SystemColors.Desktop);
    public static Pen GradientActiveCaption => FromSystemColor(SystemColors.GradientActiveCaption);
    public static Pen GradientInactiveCaption => FromSystemColor(SystemColors.GradientInactiveCaption);
    public static Pen GrayText => FromSystemColor(SystemColors.GrayText);
    public static Pen Highlight => FromSystemColor(SystemColors.Highlight);
    public static Pen HighlightText => FromSystemColor(SystemColors.HighlightText);
    public static Pen HotTrack => FromSystemColor(SystemColors.HotTrack);
    public static Pen InactiveBorder => FromSystemColor(SystemColors.InactiveBorder);
    public static Pen InactiveCaption => FromSystemColor(SystemColors.InactiveCaption);
    public static Pen InactiveCaptionText => FromSystemColor(SystemColors.InactiveCaptionText);
    public static Pen Info => FromSystemColor(SystemColors.Info);
    public static Pen InfoText => FromSystemColor(SystemColors.InfoText);
    public static Pen Menu => FromSystemColor(SystemColors.Menu);
    public static Pen MenuBar => FromSystemColor(SystemColors.MenuBar);
    public static Pen MenuHighlight => FromSystemColor(SystemColors.MenuHighlight);
    public static Pen MenuText => FromSystemColor(SystemColors.MenuText);
    public static Pen ScrollBar => FromSystemColor(SystemColors.ScrollBar);
    public static Pen Window => FromSystemColor(SystemColors.Window);
    public static Pen WindowFrame => FromSystemColor(SystemColors.WindowFrame);
    public static Pen WindowText => FromSystemColor(SystemColors.WindowText);

    public static Pen FromSystemColor(Color c)
    {
        if (!c.IsSystemColor)
        {
            throw new ArgumentException($"The color {c} is not a system color.");
        }

        // Two threads asking at once may each build one; both are equal and
        // read-only, so whichever is stored last wins and nothing is lost.
        return Cache[(int)c.ToKnownColor()] ??= new Pen(c, 1f, isShared: true);
    }
}
