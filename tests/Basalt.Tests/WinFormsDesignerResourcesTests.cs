using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsDesignerResourcesTests
{
    [Fact]
    public void ReadsTextualResourceValuesFromAnActualResxDocument()
    {
        var source = """
            <root>
              <data name="Button1.Text" xml:space="preserve"><value>  Apply  </value></data>
              <data name="Button1.Enabled" type="System.Boolean, mscorlib"><value>False</value></data>
              <data name="Button1.Location" type="System.Drawing.Point, System.Drawing"><value>-5, 20</value></data>
              <data name="Button1.Size" type="System.Drawing.Size, System.Drawing.Common"><value>90, 30</value></data>
              <data name="Button1.BackColor" type="System.Drawing.Color, System.Drawing"><value>Red</value></data>
              <data name="Timer1.Interval" type="System.Int32, System.Private.CoreLib"><value>250</value></data>
            </root>
            """;
        var document = new WinFormsResourceDocument(source);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Button1.Text")!, typeof(string), out var text));
            Assert.Equal("  Apply  ", text);
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Button1.Enabled")!, typeof(bool), out var enabled));
            Assert.Equal(false, enabled);
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Button1.Location")!, typeof(Point), out var point));
            Assert.Equal(new Point(-5, 20), point);
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Button1.Size")!, typeof(Size), out var size));
            Assert.Equal(new Size(90, 30), size);
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Button1.BackColor")!, typeof(Color), out var color));
            Assert.Equal(Color.Red, color);
            Assert.True(WinFormsDesignerResources.TryRead(document.TryGet("Timer1.Interval")!, typeof(int), out var interval));
            Assert.Equal(250, interval);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ReadsInvariantArgbAndRejectsMalformedOrOutOfRangeNumbers()
    {
        var color = Entry("Color", "System.Drawing.Color", "128, 5, 10, 15");
        Assert.True(WinFormsDesignerResources.TryRead(color, typeof(Color), out var value));
        Assert.Equal(Color.FromArgb(128, 5, 10, 15), value);

        var hex = Entry("Color", "System.Drawing.Color", "#80050A0F");
        Assert.True(WinFormsDesignerResources.TryRead(hex, typeof(Color), out value));
        Assert.Equal(Color.FromArgb(128, 5, 10, 15), value);

        Assert.False(WinFormsDesignerResources.TryRead(Entry("Number", "System.Int32", "2147483648"),
            typeof(int), out value));
        Assert.Null(value);
        Assert.False(WinFormsDesignerResources.TryRead(Entry("Size", "System.Drawing.Size", "90; 30"),
            typeof(Size), out value));
        Assert.Null(value);
        Assert.False(WinFormsDesignerResources.TryRead(Entry("Color", "System.Drawing.Color", "256, 0, 0"),
            typeof(Color), out value));
        Assert.Null(value);
        Assert.False(WinFormsDesignerResources.TryRead(Entry("Color", "System.Drawing.Color", "1"),
            typeof(Color), out value));
        Assert.Null(value);
    }

    [Fact]
    public void RejectsMimeFileReferencesCustomTypesAndMismatchedFrameworkTypes()
    {
        var mime = Entry("Color", "System.Drawing.Color, System.Drawing", "Red") with
        {
            MimeType = "application/x-microsoft.net.object.binary.base64",
        };
        Assert.False(WinFormsDesignerResources.TryRead(mime, typeof(Color), out var value));
        Assert.Null(value);

        var file = Entry("Image", "System.Resources.ResXFileRef, System.Windows.Forms", "secret.png;System.Drawing.Bitmap");
        Assert.False(WinFormsDesignerResources.TryRead(file, typeof(string), out value));
        Assert.Null(value);

        var custom = Entry("Value", typeof(ExplodingValue).FullName!, "payload");
        Assert.False(WinFormsDesignerResources.TryRead(custom, typeof(ExplodingValue), out value));
        Assert.Equal(0, ExplodingConverter.Invocations);

        Assert.False(WinFormsDesignerResources.TryRead(Entry("Count", "System.String", "12"),
            typeof(int), out value));
        Assert.False(WinFormsDesignerResources.TryRead(Entry("Count", "System.Int32, Evil.Assembly", "12"),
            typeof(int), out value));
        Assert.False(WinFormsDesignerResources.TryRead(Entry("Count", null, "12"),
            typeof(int), out value));
    }

    [Fact]
    public void WritesWhitelistValuesAsInvariantTextThatTheReaderRoundTrips()
    {
        var values = new (object Value, Type Type)[]
        {
            ("  Apply  ", typeof(string)),
            (-250, typeof(int)),
            (true, typeof(bool)),
            (new Point(-5, 20), typeof(Point)),
            (new Size(90, 30), typeof(Size)),
            (Color.FromArgb(128, 5, 10, 15), typeof(Color)),
        };
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            var document = new WinFormsResourceDocument("<root></root>");
            foreach (var (value, type) in values)
            {
                Assert.True(WinFormsDesignerResources.TryWrite(value, type, out var text, out var typeName));
                document = new WinFormsResourceDocument(document.SetValue(type.FullName!, text, typeName));
                var entry = Assert.IsType<WinFormsResourceEntry>(document.TryGet(type.FullName!));
                Assert.True(WinFormsDesignerResources.TryRead(entry, type, out var restored));
                Assert.Equal(value, restored);
            }

            Assert.Equal("-250", document.TryGet(typeof(int).FullName!)!.Value);
            Assert.Equal("-5, 20", document.TryGet(typeof(Point).FullName!)!.Value);
            Assert.Equal("128, 5, 10, 15", document.TryGet(typeof(Color).FullName!)!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void KnownAndEmptyColorsKeepTheirIdentityAcrossResourceRoundTrip()
    {
        foreach (var color in new[] { Color.Red, SystemColors.Control, Color.Empty })
        {
            Assert.True(WinFormsDesignerResources.TryWrite(color, typeof(Color), out var text, out var typeName));
            var entry = Entry("Color", typeName, text);
            Assert.True(WinFormsDesignerResources.TryRead(entry, typeof(Color), out var restored));
            var actual = Assert.IsType<Color>(restored);
            Assert.Equal(color, actual);
            Assert.Equal(color.Name, actual.Name);
            Assert.Equal(color.IsKnownColor, actual.IsKnownColor);
            Assert.Equal(color.IsEmpty, actual.IsEmpty);
        }
    }

    [Fact]
    public void WriterRejectsNullMismatchedAndUnsupportedObjects()
    {
        Assert.False(WinFormsDesignerResources.TryWrite(null, typeof(string), out var text, out var typeName));
        Assert.Equal(string.Empty, text);
        Assert.Null(typeName);
        Assert.False(WinFormsDesignerResources.TryWrite("42", typeof(int), out text, out typeName));
        Assert.False(WinFormsDesignerResources.TryWrite(new ExplodingValue(), typeof(ExplodingValue), out text, out typeName));
        Assert.False(WinFormsDesignerResources.TryWrite(Color.FromName("ApplicationCustomColor"),
            typeof(Color), out text, out typeName));
        Assert.Equal(0, ExplodingConverter.Invocations);
    }

    private static WinFormsResourceEntry Entry(string name, string? typeName, string value) =>
        new(name, typeName, null, value, null);

    [TypeConverter(typeof(ExplodingConverter))]
    private sealed class ExplodingValue
    {
    }

    private sealed class ExplodingConverter : TypeConverter
    {
        public static int Invocations { get; private set; }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            Invocations++;
            throw new InvalidOperationException("Application converters must never run during resource preview.");
        }
    }
}
