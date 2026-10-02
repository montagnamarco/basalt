using System.Collections.Specialized;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DragDrop;

/// <summary>DataObject: the carrier clipboard and drag-and-drop share for their payloads.</summary>
public class DataObjectTests
{
    [Fact]
    public void A_format_set_by_name_comes_back_by_the_same_name()
    {
        var data = new DataObject();
        data.SetData("CustomFormat", "payload");

        Assert.True(data.GetDataPresent("CustomFormat"));
        Assert.Equal("payload", data.GetData("CustomFormat"));
        Assert.False(data.GetDataPresent("SomethingElse"));
    }

    [Fact]
    public void A_format_set_by_type_comes_back_by_that_type()
    {
        var data = new DataObject();
        data.SetData(typeof(string), "hello");

        Assert.True(data.GetDataPresent(typeof(string)));
        Assert.Equal("hello", data.GetData(typeof(string)));
    }

    [Fact]
    public void Setting_a_string_without_a_format_name_infers_UnicodeText()
    {
        var data = new DataObject("free text");

        Assert.True(data.GetDataPresent(DataFormats.UnicodeText));
        Assert.Equal("free text", data.GetData(DataFormats.UnicodeText));
    }

    [Fact]
    public void Text_unicode_text_and_OEM_text_mirror_each_other_with_autoConvert()
    {
        var data = new DataObject();
        data.SetData(DataFormats.Text, autoConvert: true, "shared");

        Assert.True(data.GetDataPresent(DataFormats.UnicodeText, autoConvert: false));
        Assert.True(data.GetDataPresent(DataFormats.OemText, autoConvert: false));
        Assert.Equal("shared", data.GetData(DataFormats.UnicodeText, autoConvert: false));
    }

    [Fact]
    public void Setting_text_without_autoConvert_only_stores_the_one_format_asked_for()
    {
        var data = new DataObject();
        data.SetData(DataFormats.Text, autoConvert: false, "only text");

        Assert.True(data.GetDataPresent(DataFormats.Text, autoConvert: false));

        // Querying without autoConvert either: the other two formats were never written.
        Assert.False(data.GetDataPresent(DataFormats.UnicodeText, autoConvert: false));
    }

    [Fact]
    public void GetFormats_lists_every_format_set_and_its_text_aliases()
    {
        var data = new DataObject();
        data.SetText("hi");

        var formats = data.GetFormats();

        Assert.Contains(DataFormats.Text, formats);
        Assert.Contains(DataFormats.UnicodeText, formats);
        Assert.Contains(DataFormats.OemText, formats);
    }

    [Fact]
    public void ContainsText_and_GetText_read_back_SetText()
    {
        var data = new DataObject();
        Assert.False(data.ContainsText());

        data.SetText("round trip");

        Assert.True(data.ContainsText());
        Assert.Equal("round trip", data.GetText());
    }

    [Fact]
    public void Rtf_text_does_not_leak_into_the_plain_text_formats()
    {
        var data = new DataObject();
        data.SetText(@"{\rtf1 hello}", TextDataFormat.Rtf);

        Assert.True(data.ContainsText(TextDataFormat.Rtf));
        Assert.False(data.ContainsText());
    }

    [Fact]
    public void A_file_drop_list_round_trips_through_its_own_helpers()
    {
        var data = new DataObject();
        var files = new StringCollection();
        files.AddRange(["C:\\one.txt", "C:\\two.txt"]);

        data.SetFileDropList(files);

        Assert.True(data.ContainsFileDropList());
        Assert.Equal(files.Count, data.GetFileDropList().Count);
        Assert.Equal("C:\\one.txt", data.GetFileDropList()[0]);
    }

    [Fact]
    public void TryGetData_fails_without_throwing_when_the_format_is_absent()
    {
        var data = new DataObject();

        Assert.False(data.TryGetData("Missing", out string _));

        data.SetData("Present", "value");

        Assert.True(data.TryGetData("Present", out string value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void The_extension_method_reads_a_plain_IDataObject_the_same_way()
    {
        IDataObject data = new DataObject();
        data.SetData("Present", 42);

        Assert.True(data.TryGetData("Present", out int found));
        Assert.Equal(42, found);
        Assert.False(data.TryGetData("Missing", out int _));
    }

    [Fact]
    public void DataFormats_GetFormat_gives_the_same_id_for_the_same_name_every_time()
    {
        var first = DataFormats.GetFormat("My.Custom.Format");
        var second = DataFormats.GetFormat("My.Custom.Format");

        Assert.Equal(first.Id, second.Id);
        Assert.Same(DataFormats.GetFormat(first.Id), first);
    }

    [Fact]
    public void DataFormats_predefined_names_keep_their_classic_Windows_ids()
    {
        Assert.Equal(1, DataFormats.GetFormat(DataFormats.Text).Id);
        Assert.Equal(13, DataFormats.GetFormat(DataFormats.UnicodeText).Id);
        Assert.Equal(15, DataFormats.GetFormat(DataFormats.FileDrop).Id);
    }
}
