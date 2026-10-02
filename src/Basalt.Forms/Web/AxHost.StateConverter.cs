using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>Round-trips an AxHost.State to and from the byte array a .resx file stores it as.</summary>
    public class StateConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is byte[] buffer ? new State(buffer) : base.ConvertFrom(context, culture, value);

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(byte[]) && value is State state
                ? state.GetBuffer()
                : base.ConvertTo(context, culture, value, destinationType);
    }
}
