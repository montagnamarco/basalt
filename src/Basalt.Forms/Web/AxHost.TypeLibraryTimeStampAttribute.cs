using System.Globalization;

namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>The timestamp of the type library a generated ActiveX wrapper class was built from.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TypeLibraryTimeStampAttribute : Attribute
    {
        public TypeLibraryTimeStampAttribute(string timestamp)
        {
            Value = DateTime.Parse(timestamp, CultureInfo.InvariantCulture);
        }

        public DateTime Value { get; }
    }
}
