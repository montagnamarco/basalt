using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Only known portable values cross the OS clipboard boundary.</summary>
internal static class ClipboardDataTransfer
{
    private static readonly DataFormat<byte[]> BasaltFormat =
        DataFormat.CreateBytesApplicationFormat("basalt.forms.data.v1");
    private static readonly DataFormat<string> HtmlFormat = PlatformTextFormat(
        "public.html", "HTML Format", "text/html");
    private static readonly DataFormat<string> CsvFormat = PlatformTextFormat(
        "public.comma-separated-values-text", "Csv", "text/csv");

    internal static DataTransfer Export(DataObject data, IStorageProvider? storageProvider = null)
    {
        var transfer = new DataTransfer();
        var item = new DataTransferItem();
        var entries = new List<PortableEntry>();

        foreach (var (format, value) in data.StoredEntries)
        {
            if (data.TryExportJson(format, out var typeIdentity, out var jsonBytes))
            {
                entries.Add(new PortableEntry(format, "json", null, jsonBytes, typeIdentity));
            }
            else if (value is string text)
            {
                entries.Add(new PortableEntry(format, "string", text, null, null));
            }
            else if (value is byte[] bytes)
            {
                entries.Add(new PortableEntry(format, "bytes", null, bytes, null));
            }
        }

        if (data.ContainsText()) item.SetText(data.GetText());
        if (data.GetData(DataFormats.Html, autoConvert: false) is string html)
            item.Set(HtmlFormat, HtmlClipboardFormat.ToPlatform(html, OperatingSystem.IsWindows()));
        if (data.GetData(DataFormats.CommaSeparatedValue, autoConvert: false) is string csv) item.Set(CsvFormat, csv);

        if (data.ContainsImage() && data.GetImage() is Image image)
        {
            using var encoded = new MemoryStream();
            image.Save(encoded, ImageFormat.Png);
            encoded.Position = 0;
            item.SetBitmap(new AvaloniaBitmap(encoded));
        }

        if (entries.Count > 0)
            item.Set(BasaltFormat, JsonSerializer.SerializeToUtf8Bytes(entries));

        if (item.Formats.Count > 0) transfer.Add(item);

        if (storageProvider is not null && data.ContainsFileDropList())
        {
            foreach (var entry in data.GetFileDropList())
            {
                if (entry is not string path) continue;
                var file = SynchronousWait.For(storageProvider.TryGetFileFromPathAsync(path));
                IStorageItem? stored = file;
                stored ??= SynchronousWait.For(storageProvider.TryGetFolderFromPathAsync(path));
                if (stored is not null) transfer.Add(DataTransferItem.Create(DataFormat.File, stored));
            }
        }

        return transfer;
    }

    internal static DataObject Import(IAsyncDataTransfer transfer)
    {
        var data = new DataObject();

        if (SynchronousWait.For(transfer.TryGetValueAsync(BasaltFormat)) is { Length: > 0 and <= 16_777_216 } bytes)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<PortableEntry>>(bytes);
                if (entries is { Count: <= 1024 })
                {
                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Format) || entry.Format.Length > 256) continue;
                        switch (entry.Kind)
                        {
                            case "string" when entry.Text is not null:
                                data.SetData(entry.Format, autoConvert: false, entry.Text);
                                break;
                            case "bytes" when entry.Bytes is not null:
                                data.SetData(entry.Format, autoConvert: false, entry.Bytes);
                                break;
                            case "json" when entry.Bytes is not null && entry.TypeIdentity is { Length: > 0 and <= 1024 }:
                                data.SetImportedJson(entry.Format, entry.TypeIdentity, entry.Bytes);
                                break;
                        }
                    }
                }
            }
            catch (JsonException) { /* Ignore a malformed application clipboard payload. */ }
        }

        if (SynchronousWait.For(transfer.TryGetValueAsync(DataFormat.Text)) is string text)
            data.SetText(text);
        if (SynchronousWait.For(transfer.TryGetValueAsync(HtmlFormat)) is string html
            && !data.GetDataPresent(DataFormats.Html, autoConvert: false))
            data.SetData(DataFormats.Html, autoConvert: false,
                HtmlClipboardFormat.FromPlatform(html, OperatingSystem.IsWindows()));
        if (SynchronousWait.For(transfer.TryGetValueAsync(CsvFormat)) is string csv)
            data.SetData(DataFormats.CommaSeparatedValue, autoConvert: false, csv);

        if (SynchronousWait.For(transfer.TryGetValueAsync(DataFormat.Bitmap)) is AvaloniaBitmap bitmap)
        {
            using var encoded = new MemoryStream();
            bitmap.Save(encoded, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            encoded.Position = 0;
            data.SetImage(new DrawingBitmap(encoded));
        }

        var files = SynchronousWait.For(transfer.TryGetValuesAsync(DataFormat.File));
        if (files is { Length: > 0 })
        {
            var paths = new StringCollection();
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is not null) paths.Add(path);
            }

            if (paths.Count > 0) data.SetFileDropList(paths);
        }

        return data;
    }

    private static DataFormat<string> PlatformTextFormat(string macOs, string windows, string linux) =>
        DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? macOs :
            OperatingSystem.IsWindows() ? windows : linux);

    private sealed record PortableEntry(string Format, string Kind, string? Text, byte[]? Bytes, string? TypeIdentity);
}
