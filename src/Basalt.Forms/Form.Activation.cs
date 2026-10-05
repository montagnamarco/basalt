using System.ComponentModel;

namespace System.Windows.Forms;

public partial class Form
{
    [Browsable(false)]
    protected virtual bool ShowWithoutActivation => false;
}
