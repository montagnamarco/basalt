using System.Collections.ObjectModel;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>Adds the command line to ApplicationBase, as a console (or any non-UI) VB application sees it.</summary>
public class ConsoleApplicationBase : ApplicationBase
{
    private ReadOnlyCollection<string>? _commandLineArgs;

    public ConsoleApplicationBase()
    {
    }

    /// <summary>The process's own arguments until something (WindowsFormsApplicationBase.Run) sets InternalCommandLine.</summary>
    public ReadOnlyCollection<string> CommandLineArgs =>
        _commandLineArgs ??= new ReadOnlyCollection<string>(Environment.GetCommandLineArgs().Skip(1).ToArray());

    protected ReadOnlyCollection<string> InternalCommandLine
    {
        set => _commandLineArgs = value;
    }
}
