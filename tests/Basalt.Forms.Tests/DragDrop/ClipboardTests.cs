using System.Windows.Forms;

namespace Basalt.Forms.Tests.DragDrop;

/// <summary>
/// Clipboard built on DataObject: text still goes through the platform
/// clipboard (PlatformClipboardTests elsewhere covers that path); everything
/// else is process-local, proved here without touching the OS clipboard.
/// </summary>
public class ClipboardTests
{
    [Fact]
    public void SetDataObject_and_GetDataObject_round_trip_a_custom_format()
    {
        var data = new DataObject();
        data.SetData("Basalt.Test.Format", "process-local value");

        Clipboard.SetDataObject(data);
        var roundTripped = Clipboard.GetDataObject();

        Assert.True(roundTripped.GetDataPresent("Basalt.Test.Format"));
        Assert.Equal("process-local value", roundTripped.GetData("Basalt.Test.Format"));
    }

    [Fact]
    public void ContainsData_answers_for_the_format_just_set_and_not_for_another_one()
    {
        Clipboard.SetDataObject(new DataObject("Basalt.Other.Format", "value"));

        Assert.True(Clipboard.ContainsData("Basalt.Other.Format"));
        Assert.False(Clipboard.ContainsData("Basalt.Nonexistent.Format"));
    }

    [Fact]
    public void SetDataObject_with_a_plain_object_wraps_it_before_storing()
    {
        Clipboard.SetDataObject(123);

        Assert.True(Clipboard.TryGetData(out int found));
        Assert.Equal(123, found);
    }

    [Fact]
    public void SetData_by_format_name_is_readable_through_GetDataObject()
    {
        Clipboard.SetData("Basalt.Named.Format", "named value");

        Assert.True(Clipboard.ContainsData("Basalt.Named.Format"));
        Assert.Equal("named value", Clipboard.GetDataObject().GetData("Basalt.Named.Format"));
    }

    [Fact]
    public void TryGetData_with_an_explicit_format_finds_what_was_set_under_it()
    {
        var data = new DataObject();
        data.SetData("Basalt.Typed.Format", "typed value");
        Clipboard.SetDataObject(data);

        Assert.True(Clipboard.TryGetData("Basalt.Typed.Format", out string value));
        Assert.Equal("typed value", value);
        Assert.False(Clipboard.TryGetData("Basalt.Typed.Format.Missing", out string _));
    }

    [Fact]
    public void SetDataObject_replaces_a_custom_format_that_came_before_it()
    {
        Clipboard.SetDataObject(new DataObject("Basalt.First", "first"));
        Clipboard.SetDataObject(new DataObject("Basalt.Second", "second"));

        Assert.False(Clipboard.ContainsData("Basalt.First"));
        Assert.True(Clipboard.ContainsData("Basalt.Second"));
    }

    [Fact]
    public void Clear_empties_the_process_local_data_too()
    {
        Clipboard.SetDataObject(new DataObject("Basalt.Cleared", "value"));
        Assert.True(Clipboard.ContainsData("Basalt.Cleared"));

        Clipboard.Clear();

        Assert.False(Clipboard.ContainsData("Basalt.Cleared"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Data_without_text_set_after_text_replaces_the_text_too()
    {
        Clipboard.SetText("hello");
        var data = new DataObject();
        data.SetData("Basalt.Test.Format", 42);

        Clipboard.SetDataObject(data);

        Assert.False(Clipboard.ContainsText());
        Assert.True(Clipboard.ContainsData("Basalt.Test.Format"));
    }
}
