using System.Runtime.InteropServices;
using Windows.Win32.System.Com;

namespace System.Windows.Forms;

[ComVisible(true)]
public partial class AccessibleObject : IDispatch.Interface
{
    private const int ENotImplemented = unchecked((int)0x80004001);

    int IDispatch.Interface.GetTypeInfoCount(out uint count)
    {
        count = 0;
        return 0;
    }

    int IDispatch.Interface.GetTypeInfo(uint index, uint locale, out IntPtr typeInfo)
    {
        typeInfo = IntPtr.Zero;
        return ENotImplemented;
    }

    int IDispatch.Interface.GetIDsOfNames(ref Guid interfaceId, IntPtr names, uint count, uint locale, IntPtr dispatchIds) =>
        ENotImplemented;

    int IDispatch.Interface.Invoke(int dispatchId, ref Guid interfaceId, uint locale, ushort flags,
        IntPtr arguments, IntPtr result, IntPtr exception, IntPtr argumentError) => ENotImplemented;
}
