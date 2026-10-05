using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

[Serializable]
public class OwnerDrawPropertyBag : MarshalByRefObject, ISerializable
{
    private OwnerDrawPropertyBag() { }

    protected OwnerDrawPropertyBag(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        BackColor = (Color)info.GetValue("BackColor", typeof(Color))!;
        ForeColor = (Color)info.GetValue("ForeColor", typeof(Color))!;
        Font = (Font?)info.GetValue("Font", typeof(Font));
    }

    public Color BackColor { get; set; }
    public Color ForeColor { get; set; }
    public Font? Font { get; set; }

    public static OwnerDrawPropertyBag Copy(OwnerDrawPropertyBag? value) => new()
    {
        BackColor = value?.BackColor ?? Color.Empty,
        ForeColor = value?.ForeColor ?? Color.Empty,
        // The bag borrows the font; copying styles must not change its lifetime.
        Font = value?.Font,
    };

    public virtual bool IsEmpty() => BackColor.IsEmpty && ForeColor.IsEmpty && Font is null;

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue("BackColor", BackColor);
        info.AddValue("ForeColor", ForeColor);
        info.AddValue("Font", Font, typeof(Font));
    }
}
