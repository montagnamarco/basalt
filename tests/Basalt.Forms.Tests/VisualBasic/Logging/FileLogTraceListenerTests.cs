using System.Diagnostics;
using Microsoft.VisualBasic.Logging;

namespace Basalt.Forms.Tests.VisualBasic.Logging;

/// <summary>
/// FileLogTraceListener writing real files in a temp directory: where it
/// writes (Location/CustomLocation), how it rotates (LogFileCreationSchedule
/// and MaxFileSize), whether it appends or overwrites, and the delimiter
/// TraceEvent joins its fields with.
/// </summary>
public class FileLogTraceListenerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-filelog-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private FileLogTraceListener NewListener(string name = "TestLog") => new(name)
    {
        Location = LogFileLocation.Custom,
        CustomLocation = _directory,
    };

    [Fact]
    public void Writes_under_the_custom_location_given()
    {
        using var listener = NewListener();

        Assert.StartsWith(_directory, listener.FullLogFileName);
    }

    [Fact]
    public void Write_then_WriteLine_land_in_the_file_on_disk()
    {
        using var listener = NewListener();

        listener.Write("first");
        listener.WriteLine("second");
        listener.Flush();

        var text = File.ReadAllText(listener.FullLogFileName);
        Assert.Equal("first" + "second" + Environment.NewLine, text);
    }

    [Fact]
    public void Append_keeps_earlier_content_when_a_new_listener_opens_the_same_file()
    {
        using (var first = NewListener())
        {
            first.Append = true;
            first.WriteLine("line one");
        }

        using var second = NewListener();
        second.Append = true;
        second.WriteLine("line two");
        second.Flush();

        var text = File.ReadAllText(second.FullLogFileName);
        Assert.Contains("line one", text);
        Assert.Contains("line two", text);
    }

    [Fact]
    public void Turning_Append_off_starts_the_file_fresh()
    {
        using (var first = NewListener())
        {
            first.Append = false;
            first.WriteLine("will be overwritten");
        }

        using var second = NewListener();
        second.Append = false;
        second.WriteLine("only this remains");
        second.Flush();

        var text = File.ReadAllText(second.FullLogFileName);
        Assert.DoesNotContain("will be overwritten", text);
        Assert.Contains("only this remains", text);
    }

    [Fact]
    public void A_file_past_MaxFileSize_rolls_over_to_a_numbered_file()
    {
        using var listener = NewListener();
        listener.MaxFileSize = 50; // small enough that a handful of lines overflow it.

        var firstFile = listener.FullLogFileName;
        for (var i = 0; i < 10; i++) listener.WriteLine($"line number {i} padded with filler text");
        listener.Flush();

        var secondFile = listener.FullLogFileName;

        Assert.NotEqual(firstFile, secondFile);
        Assert.True(File.Exists(firstFile));
        Assert.True(File.Exists(secondFile));
        Assert.True(new FileInfo(firstFile).Length <= listener.MaxFileSize + 100); // the line that tipped it over still lands in the new file.
    }

    [Fact]
    public void Daily_schedule_names_the_file_with_todays_date()
    {
        using var listener = NewListener();
        listener.LogFileCreationSchedule = LogFileCreationScheduleOption.Daily;

        var expectedSuffix = DateTime.Now.ToString("yyyy-MM-dd");
        Assert.Contains(expectedSuffix, listener.FullLogFileName);
    }

    [Fact]
    public void Weekly_schedule_names_the_file_with_the_start_of_this_week()
    {
        using var listener = NewListener();
        listener.LogFileCreationSchedule = LogFileCreationScheduleOption.Weekly;

        var startOfWeek = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek).Date;
        Assert.Contains(startOfWeek.ToString("yyyy-MM-dd"), listener.FullLogFileName);
    }

    [Fact]
    public void TraceEvent_joins_its_fields_with_the_configured_delimiter()
    {
        using var listener = NewListener();
        listener.Delimiter = "|";

        listener.TraceEvent(new TraceEventCache(), "MySource", TraceEventType.Warning, 42, "something happened");
        listener.Flush();

        var line = File.ReadAllText(listener.FullLogFileName).TrimEnd();
        var fields = line.Split('|');

        Assert.Equal("MySource", fields[1]);
        Assert.Equal("Warning", fields[2]);
        Assert.Equal("42", fields[3]);
        Assert.Equal("something happened", fields[4]);
    }

    [Fact]
    public void Changing_the_delimiter_changes_how_many_fields_a_plain_comma_message_splits_into()
    {
        // Reintroducing the defect once: a hard-coded "," delimiter instead of
        // the Delimiter property would make this line split on the comma
        // already inside the message, breaking the field count below.
        using var listener = NewListener();
        listener.Delimiter = ";";

        listener.TraceEvent(new TraceEventCache(), "Source", TraceEventType.Information, 1, "a, b, c");
        listener.Flush();

        var line = File.ReadAllText(listener.FullLogFileName).TrimEnd();
        Assert.Equal(5, line.Split(';').Length);
        Assert.Equal("a, b, c", line.Split(';')[4]);
    }

    [Fact]
    public void Lines_written_from_many_threads_while_rotating_all_arrive_whole()
    {
        using var listener = NewListener();
        listener.MaxFileSize = 2_000;
        const int threads = 8;
        const int linesPerThread = 200;

        Parallel.For(0, threads, thread =>
        {
            for (var line = 0; line < linesPerThread; line++) listener.WriteLine($"thread {thread:D2} line {line:D3} end");
        });
        listener.Close();

        var lines = Directory.GetFiles(_directory).SelectMany(File.ReadAllLines).ToList();

        Assert.Equal(threads * linesPerThread, lines.Count);
        Assert.All(lines, line => Assert.Matches(@"^thread \d{2} line \d{3} end$", line));
        Assert.Equal(threads * linesPerThread, lines.Distinct().Count());
    }

    [Fact]
    public void Without_AutoFlush_rotation_still_counts_what_is_buffered()
    {
        using var listener = NewListener();
        listener.AutoFlush = false;
        listener.MaxFileSize = 500;

        for (var line = 0; line < 100; line++) listener.WriteLine($"line {line:D3} of a buffered log");
        listener.Close();

        Assert.All(Directory.GetFiles(_directory), file => Assert.True(new FileInfo(file).Length <= 500, $"{file} grew past MaxFileSize."));
    }
}
