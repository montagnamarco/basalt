namespace System.Drawing.Printing;

/// <summary>Raised once per page before it is drawn, so a handler can adjust that page's settings.</summary>
public class QueryPageSettingsEventArgs : PrintEventArgs
{
    private PageSettings _pageSettings;

    public QueryPageSettingsEventArgs(PageSettings pageSettings)
    {
        ArgumentNullException.ThrowIfNull(pageSettings);
        _pageSettings = pageSettings;
    }

    public PageSettings PageSettings
    {
        get => _pageSettings;
        set => _pageSettings = value ?? throw new ArgumentNullException(nameof(value));
    }
}
