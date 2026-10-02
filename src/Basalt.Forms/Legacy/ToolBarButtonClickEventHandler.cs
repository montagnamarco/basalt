using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`ToolBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public delegate void ToolBarButtonClickEventHandler(object sender, ToolBarButtonClickEventArgs e);
