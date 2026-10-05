using System.Runtime.InteropServices.ComTypes;

namespace System.Windows.Forms;

public partial class DataObject
{
    // FORMATETC and STGMEDIUM describe OLE memory ownership. Returning our
    // managed format store through them would promise memory we cannot supply.
    void System.Runtime.InteropServices.ComTypes.IDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium) =>
        throw OleNotSupported();

    void System.Runtime.InteropServices.ComTypes.IDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) =>
        throw OleNotSupported();

    int System.Runtime.InteropServices.ComTypes.IDataObject.QueryGetData(ref FORMATETC format) =>
        throw OleNotSupported();

    int System.Runtime.InteropServices.ComTypes.IDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) =>
        throw OleNotSupported();

    void System.Runtime.InteropServices.ComTypes.IDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) =>
        throw OleNotSupported();

    IEnumFORMATETC System.Runtime.InteropServices.ComTypes.IDataObject.EnumFormatEtc(DATADIR direction) =>
        throw OleNotSupported();

    int System.Runtime.InteropServices.ComTypes.IDataObject.DAdvise(ref FORMATETC format, ADVF advf, IAdviseSink adviseSink, out int connection) =>
        throw OleNotSupported();

    void System.Runtime.InteropServices.ComTypes.IDataObject.DUnadvise(int connection) =>
        throw OleNotSupported();

    int System.Runtime.InteropServices.ComTypes.IDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise) =>
        throw OleNotSupported();

    private static PlatformNotSupportedException OleNotSupported() =>
        new("Native OLE IDataObject requires a Windows COM data transfer backend.");
}
