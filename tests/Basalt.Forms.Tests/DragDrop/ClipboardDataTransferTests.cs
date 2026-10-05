using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.DragDrop;

public class ClipboardDataTransferTests
{
    [Fact]
    public void Transfer_preserves_known_formats_and_omits_live_objects()
    {
        var source = new DataObject();
        source.SetText("plain");
        source.SetData(DataFormats.Html, autoConvert: false, "<b>rich</b>");
        source.SetData(DataFormats.CommaSeparatedValue, autoConvert: false, "a,b");
        source.SetData("Basalt.Custom", autoConvert: false, new byte[] { 1, 2, 3 });
        source.SetData("Basalt.Live", new object());

        using var transfer = ClipboardDataTransfer.Export(source);
        var imported = ClipboardDataTransfer.Import(transfer);

        Assert.Equal("plain", imported.GetText());
        Assert.Equal("<b>rich</b>", imported.GetData(DataFormats.Html));
        Assert.Equal("a,b", imported.GetData(DataFormats.CommaSeparatedValue));
        Assert.Equal(new byte[] { 1, 2, 3 }, imported.GetData("Basalt.Custom"));
        Assert.False(imported.GetDataPresent("Basalt.Live"));
    }

    [AvaloniaFact]
    public void Transfer_round_trips_bitmap_pixels()
    {
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.Magenta);
        var source = new DataObject();
        source.SetImage(bitmap);

        using var transfer = ClipboardDataTransfer.Export(source);
        var imported = ClipboardDataTransfer.Import(transfer);

        Assert.True(imported.ContainsImage());
        using var restored = Assert.IsType<Bitmap>(imported.GetImage());
        Assert.Equal(Color.Magenta.ToArgb(), restored.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Imported_json_deserializes_only_as_its_declared_generic_type()
    {
        var source = new DataObject();
        source.SetDataAsJson("customer", new DerivedCustomer { Name = "stored" });

        using var transfer = ClipboardDataTransfer.Export(source);
        var imported = ClipboardDataTransfer.Import(transfer);

        Assert.True(imported.GetDataPresent("customer"));
        Assert.False(imported.TryGetData<Customer>("customer", out _));
        Assert.True(imported.TryGetData<DerivedCustomer>("customer", out var restored));
        Assert.Equal("stored", restored.Name);
        Assert.IsType<System.Text.Json.JsonElement>(imported.GetData("customer"));
    }

    [Fact]
    public void Cf_html_offsets_address_utf8_bytes_and_are_not_double_encoded()
    {
        const string fragment = "<b>caffè ☕</b>";
        var encoded = HtmlClipboardFormat.ToPlatform(fragment, windows: true);
        var bytes = Encoding.UTF8.GetBytes(encoded);
        var start = ReadOffset(encoded, "StartFragment:");
        var end = ReadOffset(encoded, "EndFragment:");

        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Equal(bytes.Length, ReadOffset(encoded, "EndHTML:"));
        Assert.Equal(encoded, HtmlClipboardFormat.ToPlatform(encoded, windows: true));
        Assert.Equal(fragment, HtmlClipboardFormat.ToPlatform(encoded, windows: false));
    }

    [Fact]
    public void Invalid_cf_html_offsets_are_kept_as_plain_html_on_non_windows()
    {
        var valid = HtmlClipboardFormat.WrapFragment("<i>valid</i>");
        var invalid = valid.Replace("EndFragment:", "EndFragment:9999999999", StringComparison.Ordinal);

        Assert.False(HtmlClipboardFormat.TryExtractFragment(invalid, out _));
        Assert.Equal(invalid, HtmlClipboardFormat.FromPlatform(invalid, windows: false));
        Assert.Equal(invalid, HtmlClipboardFormat.ToPlatform(invalid, windows: false));
    }

    [Fact]
    public void Cf_html_without_context_accepts_minus_one_and_doctype_fragment()
    {
        const string fragment = "<!DOCTYPE html><section>più</section>";
        const string header = "Version:1.0\r\nStartHTML:-1\r\nEndHTML:-1\r\nStartFragment:{0:D10}\r\nEndFragment:{1:D10}\r\n";
        var start = Encoding.UTF8.GetByteCount(string.Format(
            System.Globalization.CultureInfo.InvariantCulture, header, 0, 0));
        var end = start + Encoding.UTF8.GetByteCount(fragment);
        var envelope = string.Format(System.Globalization.CultureInfo.InvariantCulture, header, start, end) + fragment;

        Assert.True(HtmlClipboardFormat.TryExtractFragment(envelope, out var extracted));
        Assert.Equal(fragment, extracted);
        Assert.Equal(envelope, HtmlClipboardFormat.ToPlatform(envelope, windows: true));
        Assert.Equal(fragment, HtmlClipboardFormat.ToPlatform(envelope, windows: false));
    }

    [Fact]
    public void Transfer_preserves_the_logical_cf_html_string_exactly()
    {
        var original = HtmlClipboardFormat.WrapFragment("<b>caffè</b>");
        var source = new DataObject();
        source.SetData(DataFormats.Html, autoConvert: false, original);

        using var transfer = ClipboardDataTransfer.Export(source);
        var restored = ClipboardDataTransfer.Import(transfer);

        Assert.Equal(original, restored.GetData(DataFormats.Html));
    }

    private static int ReadOffset(string html, string field)
    {
        var start = html.IndexOf(field, StringComparison.Ordinal) + field.Length;
        return int.Parse(html.AsSpan(start, 10), System.Globalization.CultureInfo.InvariantCulture);
    }

    public class Customer
    {
        public string? Name { get; set; }
    }

    public sealed class DerivedCustomer : Customer { }
}
