namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// Carries an exception that escaped a handler to My.Application.UnhandledException.
/// ExitApplication starts true: a handler sets it false to keep the application running.
/// </summary>
public class UnhandledExceptionEventArgs : System.Threading.ThreadExceptionEventArgs
{
    public UnhandledExceptionEventArgs(bool exitApplication, Exception exception)
        : base(exception)
    {
        ExitApplication = exitApplication;
    }

    public bool ExitApplication { get; set; }
}
