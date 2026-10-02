using System.Diagnostics;
using System.Reflection;

namespace Microsoft.VisualBasic.Logging;

/// <summary>
/// My.Application.Log: a TraceSource that, absent any &lt;system.diagnostics&gt;
/// configuration, writes to one default FileLogTraceListener so a VB
/// application's Log calls always go somewhere even in an unconfigured app.
/// </summary>
public class Log
{
    private readonly TraceSource _traceSource;
    private FileLogTraceListener? _defaultFileLogWriter;

    public Log() : this((Assembly.GetEntryAssembly() ?? typeof(Log).Assembly).GetName().Name ?? "Application")
    {
    }

    public Log(string logSourceName)
    {
        ArgumentNullException.ThrowIfNull(logSourceName);

        _traceSource = new TraceSource(logSourceName);

        // A TraceSource with no configured listeners ships with just the
        // default TraceListener (writes to Debug output); that is not a file,
        // so a fresh source always gets our own file listener added to it.
        if (_traceSource.Listeners.Count <= 1) InitializeWithDefaultsSinceNoConfigExists();
    }

    public FileLogTraceListener DefaultFileLogWriter => _defaultFileLogWriter ??= new FileLogTraceListener(_traceSource.Name);

    public TraceSource TraceSource => _traceSource;

    public void WriteEntry(string message) => WriteEntry(message, TraceEventType.Information);

    public void WriteEntry(string message, TraceEventType severity) => WriteEntry(message, severity, 0);

    public void WriteEntry(string message, TraceEventType severity, int id) => _traceSource.TraceEvent(severity, id, message);

    public void WriteException(Exception ex) => WriteException(ex, TraceEventType.Error, ex?.Message ?? string.Empty);

    public void WriteException(Exception ex, TraceEventType severity, string message) => WriteException(ex, severity, message, 0);

    public void WriteException(Exception ex, TraceEventType severity, string message, int id)
    {
        ArgumentNullException.ThrowIfNull(ex);

        _traceSource.TraceEvent(severity, id, $"{message}{Environment.NewLine}{ex}");
    }

    protected virtual void InitializeWithDefaultsSinceNoConfigExists()
    {
        _traceSource.Switch.Level = SourceLevels.All;
        _traceSource.Listeners.Add(DefaultFileLogWriter);
    }
}
