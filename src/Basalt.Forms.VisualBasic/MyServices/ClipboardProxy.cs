using System.Collections.Specialized;
using System.Drawing;
using System.Reflection.Metadata;
using System.Windows.Forms;

namespace Microsoft.VisualBasic.MyServices;

/// <summary>My.Computer.Clipboard: an instance forward to the static System.Windows.Forms.Clipboard, which now carries every format Clipboard itself supports (text, data, audio, images, file drop lists).</summary>
public class ClipboardProxy
{
    /// <summary>Created by My.Computer, not by applications: Microsoft's constructor is not public.</summary>
    internal ClipboardProxy()
    {
    }

    public void Clear() => Clipboard.Clear();

    public bool ContainsAudio() => Clipboard.ContainsAudio();

    public bool ContainsData(string format) => Clipboard.ContainsData(format);

    public bool ContainsFileDropList() => Clipboard.ContainsFileDropList();

    public bool ContainsImage() => Clipboard.ContainsImage();

    public bool ContainsText() => Clipboard.ContainsText();

    public bool ContainsText(TextDataFormat format) => Clipboard.ContainsText(format);

    public Stream GetAudioStream() => Clipboard.GetAudioStream();

    // Clipboard.GetData(string) is obsolete in favour of TryGetData<T>, but
    // ClipboardProxy.GetData has no generic parameter to switch to: it must
    // keep calling the obsolete member to keep its own (non-obsolete) shape.
#pragma warning disable CS0618
    public object GetData(string format) => Clipboard.GetData(format);
#pragma warning restore CS0618

    public IDataObject GetDataObject() => Clipboard.GetDataObject();

    public StringCollection GetFileDropList() => Clipboard.GetFileDropList();

    public Image GetImage() => Clipboard.GetImage();

    public string GetText() => Clipboard.GetText();

    public string GetText(TextDataFormat format) => Clipboard.GetText(format);

    public void SetAudio(byte[] audioBytes) => Clipboard.SetAudio(audioBytes);

    public void SetAudio(Stream audioStream) => Clipboard.SetAudio(audioStream);

    public void SetData(string format, object data) => Clipboard.SetData(format, data);

    /// <summary>My.Computer.Clipboard.SetDataAsJson(Of T): no equivalent on Clipboard itself, so this serializes with System.Text.Json directly and stores it as a plain data format, the way Microsoft's runtime helper does.</summary>
    public void SetDataAsJson<T>(string format, T data)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);

        var json = System.Text.Json.JsonSerializer.Serialize(data);
        Clipboard.SetData(format, json);
    }

    public void SetDataObject(DataObject data) => Clipboard.SetDataObject(data);

    public void SetFileDropList(StringCollection filePaths) => Clipboard.SetFileDropList(filePaths);

    public void SetImage(Image image) => Clipboard.SetImage(image);

    public void SetText(string text) => Clipboard.SetText(text);

    public void SetText(string text, TextDataFormat format) => Clipboard.SetText(text, format);

    public bool TryGetData<T>(string format, out T data) => Clipboard.TryGetData(format, out data);

    public bool TryGetData<T>(string format, Func<TypeName, Type> resolver, out T data) => Clipboard.TryGetData(format, resolver, out data);
}
