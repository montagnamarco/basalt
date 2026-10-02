using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// The system clipboard. Text goes through the platform clipboard, so another
/// process can read it; every other format lives only in this process, as a
/// <see cref="DataObject"/>, because this platform has no OS-level clipboard
/// for bitmaps, file lists or custom formats to put it on.
/// </summary>
public static class Clipboard
{
    private static DataObject s_data = new();

    public static void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        PlatformClipboard.SetText(text);
        s_data = new DataObject();
        s_data.SetText(text);
    }

    public static void SetText(string text, TextDataFormat format)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var data = new DataObject();
        data.SetText(text, format);
        SetDataObject(data);
    }

    public static string GetText() => CurrentData().GetText();

    public static string GetText(TextDataFormat format) =>
        format is TextDataFormat.Text or TextDataFormat.UnicodeText ? GetText() : CurrentData().GetText(format);

    public static bool ContainsText() => CurrentData().ContainsText();

    public static bool ContainsText(TextDataFormat format) =>
        format is TextDataFormat.Text or TextDataFormat.UnicodeText ? ContainsText() : CurrentData().ContainsText(format);

    public static bool ContainsData(string? format) => format is not null && CurrentData().GetDataPresent(format);

    [Obsolete("Clipboard.GetData(string) method is obsolete. Use Clipboard.TryGetData<T> methods instead.")]
    public static object GetData(string format) => CurrentData().GetData(format);

    public static void SetData(string format, object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var wrapped = new DataObject();
        wrapped.SetData(format, data);
        SetDataObject(wrapped);
    }

    public static IDataObject GetDataObject() => CurrentData();

    public static void SetDataObject(object data) => SetDataObject(data, copy: false);

    public static void SetDataObject(object data, bool copy)
    {
        ArgumentNullException.ThrowIfNull(data);

        s_data = data as DataObject ?? new DataObject(data);

        // The platform clipboard only carries text; every other format stays process-local. Data
        // without text still replaces what was there, or old text would come back on the next read.
        if (s_data.ContainsText()) PlatformClipboard.SetText(s_data.GetText());
        else PlatformClipboard.Clear();
    }

    /// <summary>The retries exist on Windows for clipboard contention between processes; nothing to retry here.</summary>
    public static void SetDataObject(object data, bool copy, int retryTimes, int retryDelay) => SetDataObject(data, copy);

    public static bool ContainsAudio() => CurrentData().ContainsAudio();

    public static Stream GetAudioStream() => CurrentData().GetAudioStream();

    public static void SetAudio(byte[] audioBytes)
    {
        var data = new DataObject();
        data.SetAudio(audioBytes);
        SetDataObject(data);
    }

    public static void SetAudio(Stream audioStream)
    {
        var data = new DataObject();
        data.SetAudio(audioStream);
        SetDataObject(data);
    }

    public static bool ContainsFileDropList() => CurrentData().ContainsFileDropList();

    public static StringCollection GetFileDropList() => CurrentData().GetFileDropList();

    public static void SetFileDropList(StringCollection filePaths)
    {
        var data = new DataObject();
        data.SetFileDropList(filePaths);
        SetDataObject(data);
    }

    public static bool ContainsImage() => CurrentData().ContainsImage();

    public static Image GetImage() => CurrentData().GetImage();

    public static void SetImage(Image image)
    {
        var data = new DataObject();
        data.SetImage(image);
        SetDataObject(data);
    }

    public static bool TryGetData<T>(out T data) => CurrentData().TryGetData(out data);

    public static bool TryGetData<T>(string format, out T data) => CurrentData().TryGetData(format, out data);

    public static bool TryGetData<T>(string format, Func<Reflection.Metadata.TypeName, Type> resolver, out T data) =>
        CurrentData().TryGetData(format, resolver, autoConvert: true, out data);

    public static void Clear()
    {
        PlatformClipboard.Clear();
        s_data = new DataObject();
    }

    /// <summary>
    /// The process-local data, with its text formats refreshed from whatever
    /// is on the platform clipboard now. A merge, not a replace: a control
    /// that copies through <see cref="PlatformClipboard"/> directly (TextBoxBase
    /// and the other text controls bypass this class for their own copy and
    /// paste) must not erase a format <see cref="SetDataObject"/> put here.
    /// </summary>
    private static DataObject CurrentData()
    {
        var text = PlatformClipboard.GetText();
        if (!string.IsNullOrEmpty(text) && s_data.GetText() != text) s_data.SetText(text);

        return s_data;
    }
}
