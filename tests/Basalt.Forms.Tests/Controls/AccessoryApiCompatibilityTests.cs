using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class AccessoryApiCompatibilityTests
{
    [Fact]
    public void Column_header_disposal_detaches_it_and_converter_shows_caption()
    {
        using var list = new ListView();
        var header = list.Columns.Add("Name", 80);

        Assert.Equal("Name", new ColumnHeaderConverter().ConvertTo(header, typeof(string)));

        header.Dispose();

        Assert.Empty(list.Columns);
        Assert.Null(header.ListView);
    }

    [Fact]
    public void File_dialog_custom_places_have_stable_collection_and_reset()
    {
        using var dialog = new OpenFileDialog();
        var places = dialog.CustomPlaces;
        places.Add("/tmp/recent");

        Assert.Same(places, dialog.CustomPlaces);
        Assert.Equal("/tmp/recent", places[0].Path);

        dialog.Reset();
        Assert.Empty(places);
    }

    [Fact]
    public void Opacity_converter_parses_culture_specific_percentages_and_formats_whole_percent()
    {
        var converter = new OpacityConverter();
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(0.375, converter.ConvertFrom(null, culture, "37,5%"));
        // WinForms formats (int)(value * 100) + "%": truncated, no decimals.
        Assert.Equal("37%", converter.ConvertTo(null, culture, 0.375, typeof(string)));
    }

    [Fact]
    public void Native_ime_functions_refuse_synthetic_handles()
    {
        using var control = new TextBox();
        var handle = control.Handle;

        Assert.False(ImeModeConversion.IsCurrentConversionTableSupported);
        Assert.Empty(ImeModeConversion.ImeModeConversionBits);
        Assert.Throws<PlatformNotSupportedException>(() => ImeContext.Enable(handle));
        Assert.Throws<PlatformNotSupportedException>(() => ImeContext.GetImeMode(handle));
    }

    [Fact]
    public void Native_window_subclass_can_dispatch_a_managed_message()
    {
        var window = new DispatchingWindow();
        var message = Message.Create(IntPtr.Zero, 0x0400, IntPtr.Zero, IntPtr.Zero);

        window.Dispatch(ref message);

        Assert.Equal(1, window.Calls);
        Assert.Equal((IntPtr)42, message.Result);
    }

    [Fact]
    public void Data_object_exposes_OLE_identity_but_rejects_native_medium_requests()
    {
        var data = new DataObject();
        data.SetText("local text");
        var ole = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
        var format = new FORMATETC();

        Assert.Throws<PlatformNotSupportedException>(() => ole.QueryGetData(ref format));
        Assert.Equal("local text", data.GetText());
    }

    private sealed class DispatchingWindow : NativeWindow
    {
        internal int Calls { get; private set; }

        internal void Dispatch(ref Message message) => WndProc(ref message);

        protected override void WndProc(ref Message message)
        {
            Calls++;
            message.Result = (IntPtr)42;
        }
    }
}
