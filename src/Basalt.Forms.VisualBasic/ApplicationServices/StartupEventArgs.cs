using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>Carries the command line to My.Application.Startup; Cancel there ends the application.</summary>
public class StartupEventArgs : CancelEventArgs
{
    public StartupEventArgs(ReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        CommandLine = args;
    }

    public ReadOnlyCollection<string> CommandLine { get; }
}
