namespace System.Windows.Forms;

// These declarations preserve override points while sharing the portable message dispatcher.

public partial class Button
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ButtonBase
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class CheckedListBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
    protected override void WmReflectCommand(ref Message m) => base.WmReflectCommand(ref m);
}

public partial class ComboBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ContainerControl
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class DateTimePicker
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class Form
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
    protected override void DefWndProc(ref Message m) => base.DefWndProc(ref m);
}

public partial class GroupBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class Label
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class LinkLabel
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class MaskedTextBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public sealed partial class MdiClient
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class MenuStrip
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class MonthCalendar
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class PrintPreviewControl
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class PropertyGrid
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class RichTextBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ScrollBar
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ScrollableControl
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class SplitContainer
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class StatusStrip
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class TabControl
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class TextBox
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class TextBoxBase
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ToolStrip
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ToolStripDropDown
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class TrackBar
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class UpDownBase
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class UserControl
{
    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}

public partial class ListBox
{
    protected virtual void WmReflectCommand(ref Message m)
    {
        if (((m.WParam.ToInt64() >> 16) & 0xffff) == 1) OnSelectedIndexChanged(EventArgs.Empty);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x2111) WmReflectCommand(ref m);
        else base.WndProc(ref m);
    }
}
