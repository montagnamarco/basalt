Imports System.Runtime.InteropServices

Namespace Accessibility
    ''' <summary>The managed MSAA contract, with the native IAccessible IID and dispatch identifiers.</summary>
    <ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual), TypeLibType(CType(4176, TypeLibTypeFlags))>
    Public Interface IAccessible
        <DispId(-5000)>
        ReadOnly Property accParent As <MarshalAs(UnmanagedType.IDispatch)> Object

        <DispId(-5001)>
        ReadOnly Property accChildCount As Integer

        <DispId(-5002)>
        ReadOnly Property accChild(<[In], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.IDispatch)> Object

        <DispId(-5003)>
        ReadOnly Property accName(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5004)>
        ReadOnly Property accValue(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5005)>
        ReadOnly Property accDescription(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5006)>
        ReadOnly Property accRole(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5007)>
        ReadOnly Property accState(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5008)>
        ReadOnly Property accHelp(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5009)>
        Function get_accHelpTopic(<Out, MarshalAs(UnmanagedType.BStr)> ByRef pszHelpFile As String,
                                  <[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As Integer

        <DispId(-5010)>
        ReadOnly Property accKeyboardShortcut(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5011)>
        ReadOnly Property accFocus As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5012)>
        ReadOnly Property accSelection As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5013)>
        ReadOnly Property accDefaultAction(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object) As <MarshalAs(UnmanagedType.BStr)> String

        <DispId(-5014)>
        Sub accSelect(<[In]> flagsSelect As Integer,
                      <[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object)

        <DispId(-5015)>
        Sub accLocation(<Out> ByRef pxLeft As Integer, <Out> ByRef pyTop As Integer,
                        <Out> ByRef pcxWidth As Integer, <Out> ByRef pcyHeight As Integer,
                        <[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object)

        <DispId(-5016)>
        Function accNavigate(<[In]> navDir As Integer,
                             <[In], [Optional], MarshalAs(UnmanagedType.Struct)> varStart As Object) As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5017)>
        Function accHitTest(<[In]> xLeft As Integer, <[In]> yTop As Integer) As <MarshalAs(UnmanagedType.Struct)> Object

        <DispId(-5018)>
        Sub accDoDefaultAction(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object)

        <DispId(-5003)>
        Sub set_accName(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object,
                        <[In], MarshalAs(UnmanagedType.BStr)> pszName As String)

        <DispId(-5004)>
        Sub set_accValue(<[In], [Optional], MarshalAs(UnmanagedType.Struct)> varChild As Object,
                         <[In], MarshalAs(UnmanagedType.BStr)> pszValue As String)
    End Interface
End Namespace
