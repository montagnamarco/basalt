using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class OwnerDrawPropertyBagTests
{
    [Fact]
    public void Copy_preserves_styles_without_sharing_mutable_bag()
    {
        using var font = new Font("Arial", 10);
        var original = OwnerDrawPropertyBag.Copy(null);
        Assert.True(original.IsEmpty());
        original.BackColor = Color.Red;
        original.ForeColor = Color.Blue;
        original.Font = font;

        var copy = OwnerDrawPropertyBag.Copy(original);
        Assert.False(copy.IsEmpty());
        Assert.Equal(Color.Red, copy.BackColor);
        Assert.Equal(Color.Blue, copy.ForeColor);
        Assert.Same(font, copy.Font);
        copy.BackColor = Color.Green;
        Assert.Equal(Color.Red, original.BackColor);
    }

    [Fact]
    public void Serialization_preserves_empty_and_explicit_styles()
    {
        var original = OwnerDrawPropertyBag.Copy(null);
        original.ForeColor = Color.Purple;
#pragma warning disable SYSLIB0050
        var info = new SerializationInfo(typeof(OwnerDrawPropertyBag), new FormatterConverter());
#pragma warning restore SYSLIB0050
        ((ISerializable)original).GetObjectData(info, default);
        var restored = new RestoredBag(info);
        Assert.Equal(Color.Empty, restored.BackColor);
        Assert.Equal(Color.Purple, restored.ForeColor);
        Assert.Null(restored.Font);
        Assert.False(restored.IsEmpty());
    }

    private sealed class RestoredBag(SerializationInfo info) : OwnerDrawPropertyBag(info, default);
}
