using System.Runtime.InteropServices;

namespace Windows.Win32.System.Com;

/// <summary>The four IDispatch vtable entries following IUnknown, using pointer-sized ABI parameters.</summary>
internal static class IDispatch
{
    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Interface
    {
        [PreserveSig]
        int GetTypeInfoCount(out uint count);

        [PreserveSig]
        int GetTypeInfo(uint index, uint locale, out IntPtr typeInfo);

        [PreserveSig]
        int GetIDsOfNames(ref Guid interfaceId, IntPtr names, uint count, uint locale, IntPtr dispatchIds);

        [PreserveSig]
        int Invoke(int dispatchId, ref Guid interfaceId, uint locale, ushort flags,
            IntPtr arguments, IntPtr result, IntPtr exception, IntPtr argumentError);
    }
}
