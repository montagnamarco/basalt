using System.ComponentModel;
using System.Runtime.InteropServices;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultProperty("GridEditName")]
public class DataGridTextBox : TextBox
{
    public DataGridTextBox() => throw new PlatformNotSupportedException();

    public bool IsInEditOrNavigateMode
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public void SetDataGrid(DataGrid parentGrid) => throw new PlatformNotSupportedException();
}
