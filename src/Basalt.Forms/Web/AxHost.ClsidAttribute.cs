namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>The CLSID a generated ActiveX wrapper class was built from, recorded for tools that read it back.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ClsidAttribute : Attribute
    {
        public ClsidAttribute(string clsid)
        {
            Value = clsid;
        }

        public string Value { get; }
    }
}
