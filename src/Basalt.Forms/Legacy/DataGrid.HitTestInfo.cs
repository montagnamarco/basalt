using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public partial class DataGrid
{
    [Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public sealed class HitTestInfo
    {
        // Microsoft's reference signature has no public constructor: only
        // DataGrid can create one, and its own HitTest methods already throw.
        internal HitTestInfo()
        {
        }

        // Static field: kept as Microsoft declares it, not throwing.
        public static readonly DataGrid.HitTestInfo? Nowhere;

        public int Column => throw new PlatformNotSupportedException();

        public int Row => throw new PlatformNotSupportedException();

        public DataGrid.HitTestType Type => throw new PlatformNotSupportedException();
    }
}
