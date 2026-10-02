using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Per-monitor DPI. Basalt lays controls out in logical pixels at 96 dpi and
/// lets the platform window scale the whole canvas, so a control's device
/// DPI is always 96 and the DPI change events never fire: conversions are
/// the identity, which is what WinForms computes at 96 dpi.
/// </summary>
public partial class Control
{
    private const int LogicalDpi = 96;

    private static readonly object s_dpiChangedBeforeParentEvent = new();
    private static readonly object s_dpiChangedAfterParentEvent = new();

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int DeviceDpi => LogicalDpi;

    public int LogicalToDeviceUnits(int value) => (int)Math.Round(value * (double)DeviceDpi / LogicalDpi);

    public Size LogicalToDeviceUnits(Size value) => new(LogicalToDeviceUnits(value.Width), LogicalToDeviceUnits(value.Height));

    /// <summary>Scales a bitmap drawn for 96 dpi to the device; at 96 dpi it is left as it is.</summary>
    public void ScaleBitmapLogicalToDevice(ref Bitmap logicalBitmap)
    {
        ArgumentNullException.ThrowIfNull(logicalBitmap);

        if (DeviceDpi == LogicalDpi) return;

        logicalBitmap = new Bitmap(logicalBitmap, LogicalToDeviceUnits(logicalBitmap.Size));
    }

    public event EventHandler? DpiChangedBeforeParent
    {
        add => Events.AddHandler(s_dpiChangedBeforeParentEvent, value);
        remove => Events.RemoveHandler(s_dpiChangedBeforeParentEvent, value);
    }

    public event EventHandler? DpiChangedAfterParent
    {
        add => Events.AddHandler(s_dpiChangedAfterParentEvent, value);
        remove => Events.RemoveHandler(s_dpiChangedAfterParentEvent, value);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnDpiChangedBeforeParent(EventArgs e) => ((EventHandler?)Events[s_dpiChangedBeforeParentEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnDpiChangedAfterParent(EventArgs e) => ((EventHandler?)Events[s_dpiChangedAfterParentEvent])?.Invoke(this, e);

    /// <summary>Where a control recomputes sizes it keeps in device pixels after a DPI change; nothing to do by default.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void RescaleConstantsForDpi(int deviceDpiOld, int deviceDpiNew)
    {
    }
}
