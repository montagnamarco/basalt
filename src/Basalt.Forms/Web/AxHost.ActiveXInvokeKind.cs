namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>What InvalidActiveXStateException says went wrong: a method, a property get, or a property set.</summary>
    public enum ActiveXInvokeKind
    {
        MethodInvoke = 0,
        PropertyGet = 1,
        PropertySet = 2,
    }
}
