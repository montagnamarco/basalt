namespace System.Drawing;

/// <summary>A brush for each colour of the system's colour scheme.</summary>
/// <remarks>
/// The colours are the ones <see cref="SystemColors"/> answers with, which off
/// Windows is a fixed table in the framework: the default skin draws with the
/// same table, so a control painted with these matches the ones around it.
/// </remarks>
public static class SystemBrushes
{
    private static readonly Brush?[] Cache = new Brush?[(int)KnownColor.RebeccaPurple + 1];

    public static Brush ActiveBorder => FromSystemColor(SystemColors.ActiveBorder);
    public static Brush ActiveCaption => FromSystemColor(SystemColors.ActiveCaption);
    public static Brush ActiveCaptionText => FromSystemColor(SystemColors.ActiveCaptionText);
    public static Brush AppWorkspace => FromSystemColor(SystemColors.AppWorkspace);
    public static Brush ButtonFace => FromSystemColor(SystemColors.ButtonFace);
    public static Brush ButtonHighlight => FromSystemColor(SystemColors.ButtonHighlight);
    public static Brush ButtonShadow => FromSystemColor(SystemColors.ButtonShadow);
    public static Brush Control => FromSystemColor(SystemColors.Control);
    public static Brush ControlDark => FromSystemColor(SystemColors.ControlDark);
    public static Brush ControlDarkDark => FromSystemColor(SystemColors.ControlDarkDark);
    public static Brush ControlLight => FromSystemColor(SystemColors.ControlLight);
    public static Brush ControlLightLight => FromSystemColor(SystemColors.ControlLightLight);
    public static Brush ControlText => FromSystemColor(SystemColors.ControlText);
    public static Brush Desktop => FromSystemColor(SystemColors.Desktop);
    public static Brush GradientActiveCaption => FromSystemColor(SystemColors.GradientActiveCaption);
    public static Brush GradientInactiveCaption => FromSystemColor(SystemColors.GradientInactiveCaption);
    public static Brush GrayText => FromSystemColor(SystemColors.GrayText);
    public static Brush Highlight => FromSystemColor(SystemColors.Highlight);
    public static Brush HighlightText => FromSystemColor(SystemColors.HighlightText);
    public static Brush HotTrack => FromSystemColor(SystemColors.HotTrack);
    public static Brush InactiveBorder => FromSystemColor(SystemColors.InactiveBorder);
    public static Brush InactiveCaption => FromSystemColor(SystemColors.InactiveCaption);
    public static Brush InactiveCaptionText => FromSystemColor(SystemColors.InactiveCaptionText);
    public static Brush Info => FromSystemColor(SystemColors.Info);
    public static Brush InfoText => FromSystemColor(SystemColors.InfoText);
    public static Brush Menu => FromSystemColor(SystemColors.Menu);
    public static Brush MenuBar => FromSystemColor(SystemColors.MenuBar);
    public static Brush MenuHighlight => FromSystemColor(SystemColors.MenuHighlight);
    public static Brush MenuText => FromSystemColor(SystemColors.MenuText);
    public static Brush ScrollBar => FromSystemColor(SystemColors.ScrollBar);
    public static Brush Window => FromSystemColor(SystemColors.Window);
    public static Brush WindowFrame => FromSystemColor(SystemColors.WindowFrame);
    public static Brush WindowText => FromSystemColor(SystemColors.WindowText);

    public static Brush FromSystemColor(Color c)
    {
        if (!c.IsSystemColor)
        {
            throw new ArgumentException($"The color {c} is not a system color.");
        }

        // Two threads asking at once may each build one; both are equal and
        // read-only, so whichever is stored last wins and nothing is lost.
        return Cache[(int)c.ToKnownColor()] ??= new SolidBrush(c, isShared: true);
    }
}
