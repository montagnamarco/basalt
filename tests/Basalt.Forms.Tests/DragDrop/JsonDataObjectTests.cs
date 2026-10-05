using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basalt.Forms.Tests.DragDrop;

[Collection(SharedStateCollection.Name)]
public class JsonDataObjectTests
{
    [Fact]
    public void Json_data_round_trips_a_snapshot_without_retaining_the_source_object()
    {
        var customer = new Customer { Name = "original" };
        var data = new DataObject();
        data.SetDataAsJson("customer", customer);
        customer.Name = "changed";
        Assert.True(data.TryGetData<Customer>("customer", out var restored));
        Assert.Equal("original", restored.Name);
        Assert.NotSame(customer, restored);
        Assert.False(data.TryGetData<int>("customer", out _));
    }

    [Fact]
    public void Default_json_format_and_clipboard_support_typed_reads()
    {
        var data = new DataObject();
        data.SetDataAsJson(new Customer { Name = "default" });
        Assert.Contains(typeof(Customer).FullName!, data.GetFormats(false));
        Assert.True(data.TryGetData<Customer>(out var value));
        Assert.Equal("default", value.Name);
        try
        {
            Clipboard.SetDataAsJson("custom-text", "copied");
            Assert.True(Clipboard.TryGetData<string>("custom-text", out var text));
            Assert.Equal("copied", text);
        }
        finally { Clipboard.Clear(); }
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("UnicodeText")]
    [InlineData("System.String")]
    [InlineData("Bitmap")]
    [InlineData("FileDrop")]
    [InlineData("Csv")]
    public void Json_rejects_predefined_formats(string format)
    {
        var data = new DataObject();
        Assert.Equal("format", Assert.Throws<ArgumentException>(() => data.SetDataAsJson(format, "value")).ParamName);
        Assert.Empty(data.GetFormats(false));
    }

    [Fact]
    public void Default_string_format_is_predefined_and_json_rejects_data_objects()
    {
        var data = new DataObject();
        Assert.Equal("format", Assert.Throws<ArgumentException>(() => data.SetDataAsJson("value")).ParamName);
        Assert.Equal("data", Assert.Throws<ArgumentException>(() => data.SetDataAsJson("custom", new DataObject())).ParamName);
        Assert.Equal("data", Assert.Throws<ArgumentException>(() => data.SetDataAsJson("custom", new DerivedDataObject())).ParamName);
        Assert.Throws<InvalidOperationException>(() => new Control().DoDragDropAsJson(new DataObject(), DragDropEffects.Copy));
    }

    [Fact]
    public void Type_lookup_uses_type_full_name_for_json_data()
    {
        var data = new DataObject();
        data.SetData(DataFormats.StringFormat, autoConvert: false, "value");
        Assert.True(data.GetDataPresent(typeof(string)));
        Assert.Equal("value", data.GetData(typeof(string)));

        // A format that equals a type name is available through the Type overload.
        // System.String itself is reserved, so use a custom reference type.
        data.SetDataAsJson(new Customer { Name = "typed" });
        Assert.True(data.GetDataPresent(typeof(Customer)));
        Assert.NotNull(data.GetData(typeof(Customer)));
    }

    [Fact]
    public void Presence_checks_do_not_deserialize_json()
    {
        var data = new DataObject();
        NullOnReadConverter.ReadCount = 0;
        data.SetDataAsJson("custom", new NullOnRead());
        Assert.True(data.GetDataPresent("custom", autoConvert: false));
        Assert.Equal(0, NullOnReadConverter.ReadCount);
        Assert.Null(data.GetData("custom", autoConvert: false));
        Assert.Equal(1, NullOnReadConverter.ReadCount);
    }

    [Fact]
    public void Json_typed_reads_require_the_original_generic_type()
    {
        var data = new DataObject();
        data.SetDataAsJson("customer", new DerivedCustomer { Name = "json" });
        Assert.False(data.TryGetData<Customer>("customer", out _));
        Assert.True(data.TryGetData<DerivedCustomer>("customer", out var derived));
        Assert.Equal("json", derived.Name);

        data.SetData("live", new DerivedCustomer { Name = "live" });
        Assert.True(data.TryGetData<Customer>("live", out var live));
        Assert.Equal("live", live.Name);
    }

    public class Customer
    {
        public string? Name { get; set; }
    }

    public sealed class DerivedCustomer : Customer { }

    public sealed class DerivedDataObject : DataObject { }

    [JsonConverter(typeof(NullOnReadConverter))]
    public sealed class NullOnRead { }

    public sealed class NullOnReadConverter : JsonConverter<NullOnRead>
    {
        public static int ReadCount { get; set; }

        public override NullOnRead? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ReadCount++;
            return null;
        }

        public override void Write(Utf8JsonWriter writer, NullOnRead value, JsonSerializerOptions options) =>
            writer.WriteStringValue("payload");
    }
}
