using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Microsoft.VisualBasic.Logging;

/// <summary>
/// The TraceListener behind My.Application.Log's default file: one text file
/// per BaseFileName, grown or rotated as it is written to, the way the
/// legacy Microsoft.VisualBasic.Forms writer does.
/// </summary>
/// <remarks>
/// Rotation has two independent triggers, both checked before every write:
/// LogFileCreationSchedule starts a new file (named with today's date, or
/// the date the current week started) when the clock crosses into a new
/// day/week, and MaxFileSize starts a new, numbered file ("Name1.log",
/// "Name2.log"...) once the current one would grow past the limit. Both
/// reuse an existing file under Append, and both honour
/// DiskSpaceExhaustedBehavior when ReserveDiskSpace would be violated.
/// </remarks>
public class FileLogTraceListener : TraceListener
{
    private const long DefaultMaxFileSize = 5_000_000;
    private const long DefaultReserveDiskSpace = 10_000_000;

    /// <summary>
    /// One writer and one rotation state shared by every thread that traces
    /// through this listener: each write, flush and close holds this lock, so
    /// lines never interleave and rotation never closes a writer in use.
    /// </summary>
    private readonly Lock _gate = new();

    private StreamWriter? _writer;
    private string? _openBaseName;
    private int _openIndex;

    public FileLogTraceListener() : this(EntryAssemblyName())
    {
    }

    public FileLogTraceListener(string name) : base(name)
    {
        BaseFileName = EntryAssemblyName();
    }

    public bool Append { get; set; } = true;

    public bool AutoFlush { get; set; } = true;

    public string BaseFileName { get; set; }

    public string CustomLocation { get; set; } = Directory.GetCurrentDirectory();

    public string Delimiter { get; set; } = "\t";

    public DiskSpaceExhaustedOption DiskSpaceExhaustedBehavior { get; set; } = DiskSpaceExhaustedOption.ThrowException;

    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The path EnsureWriter would (re)open right now: the base name, today's/this week's date when scheduled, and the current rotation index.</summary>
    public string FullLogFileName => CandidateFileName(ComputeBaseName(), CurrentRotationIndexOrZero());

    public bool IncludeHostName { get; set; }

    public LogFileLocation Location { get; set; } = LogFileLocation.LocalUserApplicationDirectory;

    public LogFileCreationScheduleOption LogFileCreationSchedule { get; set; } = LogFileCreationScheduleOption.None;

    public long MaxFileSize { get; set; } = DefaultMaxFileSize;

    public long ReserveDiskSpace { get; set; } = DefaultReserveDiskSpace;

    public override void Close()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }

        base.Close();
    }

    public override void Flush()
    {
        lock (_gate)
        {
            _writer?.Flush();
        }
    }

    public override void Write(string? message) => WriteCore(message ?? string.Empty);

    public override void WriteLine(string? message) => WriteCore((message ?? string.Empty) + Environment.NewLine);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        WriteLine(FormatLine(source, eventType, id, message ?? string.Empty));

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        var message = format is null ? string.Empty : (args is null or { Length: 0 } ? format : string.Format(CultureInfo.InvariantCulture, format, args));
        WriteLine(FormatLine(source, eventType, id, message));
    }

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data) =>
        WriteLine(FormatLine(source, eventType, id, data?.ToString() ?? string.Empty));

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data) =>
        WriteLine(FormatLine(source, eventType, id, data is null ? string.Empty : string.Join(", ", data)));

    protected override string[] GetSupportedAttributes() =>
    [
        "append", "autoflush", "basefilename", "customlocation", "delimiter", "diskspaceexhaustedbehavior",
        "encoding", "includehostname", "location", "logfilecreationschedule", "maxfilesize", "reservediskspace",
    ];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        base.Dispose(disposing);
    }

    private string FormatLine(string source, TraceEventType eventType, int id, string message)
    {
        var fields = new List<string> { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
        if (IncludeHostName) fields.Add(Environment.MachineName);
        fields.Add(source);
        fields.Add(eventType.ToString());
        fields.Add(id.ToString(CultureInfo.InvariantCulture));
        fields.Add(message);

        return string.Join(Delimiter, fields);
    }

    private void WriteCore(string text)
    {
        lock (_gate)
        {
            EnsureWriter(Encoding.GetByteCount(text));
            if (_writer is null) return; // DiscardMessages: disk space is short, so this entry is simply dropped.

            _writer.Write(text);
            if (AutoFlush) _writer.Flush();
        }
    }

    /// <summary>Opens (or rotates to) the file this write belongs in, honouring the schedule, MaxFileSize and ReserveDiskSpace; sets _writer to null only when DiskSpaceExhaustedBehavior says to discard the entry.</summary>
    private void EnsureWriter(int incomingByteCount)
    {
        var baseName = ComputeBaseName();

        if (_writer is null || _openBaseName != baseName)
        {
            _openBaseName = baseName;
            _openIndex = Append ? FindAppendIndex(baseName) : 0;
            OpenWriter(baseName, _openIndex, overwrite: !Append);
        }
        else if (CurrentFileWouldExceedMaxSize(baseName, _openIndex, incomingByteCount))
        {
            _openIndex++;
            OpenWriter(baseName, _openIndex, overwrite: !Append);
        }

        if (_writer is not null && IsDiskSpaceExhausted(incomingByteCount))
        {
            if (DiskSpaceExhaustedBehavior == DiskSpaceExhaustedOption.ThrowException)
            {
                throw new IOException(
                    $"FileLogTraceListener cannot write to '{CandidateFileName(baseName, _openIndex)}': fewer than ReserveDiskSpace ({ReserveDiskSpace}) bytes would remain free.");
            }

            _writer.Dispose();
            _writer = null;
        }
    }

    private bool CurrentFileWouldExceedMaxSize(string baseName, int index, int incomingByteCount)
    {
        var path = CandidateFileName(baseName, index);

        // Without AutoFlush the open writer still holds lines the file does not show yet: count them too.
        if (_writer is not null && index == _openIndex && baseName == _openBaseName) _writer.Flush();

        var currentLength = File.Exists(path) ? new FileInfo(path).Length : 0;
        return currentLength + incomingByteCount > MaxFileSize;
    }

    private bool IsDiskSpaceExhausted(int incomingByteCount)
    {
        try
        {
            var root = Path.GetPathRoot(ResolveDirectory());
            if (string.IsNullOrEmpty(root)) return false;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace - incomingByteCount < ReserveDiskSpace;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false; // Cannot determine free space (e.g. a non-rooted or virtual path): do not block writes over it.
        }
    }

    /// <summary>With Append, reuse the highest-numbered existing file that still has room; otherwise start a fresh file at index 0.</summary>
    private int FindAppendIndex(string baseName)
    {
        var index = 0;
        while (File.Exists(CandidateFileName(baseName, index + 1))) index++;

        if (File.Exists(CandidateFileName(baseName, index)) && CurrentFileWouldExceedMaxSize(baseName, index, 0)) index++;

        return index;
    }

    private void OpenWriter(string baseName, int index, bool overwrite)
    {
        _writer?.Dispose();

        var path = CandidateFileName(baseName, index);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var mode = overwrite ? FileMode.Create : FileMode.Append;
        _writer = new StreamWriter(new FileStream(path, mode, FileAccess.Write, FileShare.Read), Encoding);
    }

    private string ComputeBaseName() => Path.Combine(ResolveDirectory(), BaseFileName + ScheduleSuffix());

    private string CandidateFileName(string baseName, int index) => baseName + (index == 0 ? string.Empty : index.ToString(CultureInfo.InvariantCulture)) + ".log";

    private int CurrentRotationIndexOrZero() => _openBaseName == ComputeBaseName() ? _openIndex : (Append ? FindAppendIndex(ComputeBaseName()) : 0);

    private string ScheduleSuffix() => LogFileCreationSchedule switch
    {
        LogFileCreationScheduleOption.Daily => "-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        LogFileCreationScheduleOption.Weekly => "-" + StartOfWeek(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static DateTime StartOfWeek(DateTime date) => date.AddDays(-(int)date.DayOfWeek).Date;

    private string ResolveDirectory() => Location switch
    {
        LogFileLocation.TempDirectory => Path.GetTempPath(),
        LogFileLocation.ExecutableDirectory => AppContext.BaseDirectory,
        LogFileLocation.CommonApplicationDirectory => CompanyProductPath(Environment.SpecialFolder.CommonApplicationData),
        LogFileLocation.Custom => CustomLocation,
        _ => CompanyProductPath(Environment.SpecialFolder.LocalApplicationData),
    };

    private static string CompanyProductPath(Environment.SpecialFolder root)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(FileLogTraceListener).Assembly;
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? EntryAssemblyName();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        var parts = new List<string> { Environment.GetFolderPath(root) };
        if (!string.IsNullOrEmpty(company)) parts.Add(company);
        parts.Add(product);
        parts.Add(version);

        return Path.Combine(parts.ToArray());
    }

    private static string EntryAssemblyName() => (Assembly.GetEntryAssembly() ?? typeof(FileLogTraceListener).Assembly).GetName().Name ?? "Application";
}
