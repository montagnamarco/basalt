using Microsoft.VisualBasic.ApplicationServices;

namespace Basalt.Forms.Tests.VisualBasic.Logging;

/// <summary>
/// My.Application.Log (ApplicationBase.Log): it must exist, be a real
/// Microsoft.VisualBasic.Logging.Log tied to this application's assembly
/// name, and be the same instance on every access - not a fresh TraceSource
/// (and a fresh default file listener) every time a VB app writes a line.
/// </summary>
/// <remarks>
/// WriteEntry itself is not exercised here: the default FileLogTraceListener
/// writes under the user's real LocalApplicationData directory, and this
/// test must not leave files behind on the machine it runs on - that path is
/// covered with a Custom location in FileLogTraceListenerTests instead.
/// </remarks>
public class ApplicationBaseLogTests
{
    [Fact]
    public void Log_is_named_after_the_application_and_cached_across_accesses()
    {
        var app = new ApplicationBase();

        var log = app.Log;

        Assert.Equal(app.Info.AssemblyName, log.TraceSource.Name);
        Assert.Same(log, app.Log);
    }

    [Fact]
    public void Log_starts_with_a_default_file_listener_already_attached()
    {
        var app = new ApplicationBase();

        var log = app.Log;

        Assert.Contains(log.DefaultFileLogWriter, log.TraceSource.Listeners.Cast<object>());
    }
}
