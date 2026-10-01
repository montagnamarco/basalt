using System.Collections.ObjectModel;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>Carries a second launch's command line to the first instance's My.Application.StartupNextInstance.</summary>
public class StartupNextInstanceEventArgs : EventArgs
{
    public StartupNextInstanceEventArgs(ReadOnlyCollection<string> args, bool bringToForegroundFlag)
    {
        ArgumentNullException.ThrowIfNull(args);

        CommandLine = args;
        BringToForeground = bringToForegroundFlag;
    }

    public ReadOnlyCollection<string> CommandLine { get; }

    public bool BringToForeground { get; set; }
}
