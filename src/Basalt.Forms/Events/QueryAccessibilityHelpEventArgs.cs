namespace System.Windows.Forms;

public class QueryAccessibilityHelpEventArgs : EventArgs
{
    public QueryAccessibilityHelpEventArgs()
    {
    }

    public QueryAccessibilityHelpEventArgs(string? helpNamespace, string? helpString, string? helpKeyword)
    {
        HelpNamespace = helpNamespace;
        HelpString = helpString;
        HelpKeyword = helpKeyword;
    }

    public string? HelpKeyword { get; set; }

    public string? HelpNamespace { get; set; }

    public string? HelpString { get; set; }
}
