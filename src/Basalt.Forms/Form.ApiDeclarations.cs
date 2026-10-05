using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

public partial class Form
{
    private Rectangle _maximizedBounds;
    private bool _applyingAutoSize;

    public override bool AutoSize
    {
        get => base.AutoSize;
        set
        {
            if (value == base.AutoSize) return;
            base.AutoSize = value;
            PerformLayout();
        }
    }

    [DefaultValue(AutoSizeMode.GrowOnly)]
    public AutoSizeMode AutoSizeMode
    {
        get => GetAutoSizeMode();
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoSizeMode));
            SetAutoSizeMode(value);
            if (AutoSize) PerformLayout();
        }
    }

    protected override ImeMode DefaultImeMode => ImeMode.NoControl;
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    protected override void Select(bool directed, bool forward) => base.Select(directed, forward);
    protected override void ScaleMinMaxSize(float x, float y, bool updateContainerSize) => base.ScaleMinMaxSize(x, y, updateContainerSize);

    protected virtual bool OnGetDpiScaledSize(int oldDpi, int newDpi, ref Size desiredSize)
    {
        if (oldDpi <= 0 || newDpi <= 0) throw new ArgumentOutOfRangeException(nameof(oldDpi));
        var ratio = (double)newDpi / oldDpi;
        desiredSize = new Size((int)Math.Round(Size.Width * ratio), (int)Math.Round(Size.Height * ratio));
        return true;
    }

    protected Rectangle MaximizedBounds
    {
        get => _maximizedBounds;
        set
        {
            if (value.Width < 0 || value.Height < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_maximizedBounds == value) return;
            _maximizedBounds = value;
            ApplyMaximizedBounds();
            OnMaximizedBoundsChanged(EventArgs.Empty);
        }
    }

    public event EventHandler? MaximizedBoundsChanged;
    protected virtual void OnMaximizedBoundsChanged(EventArgs e) => MaximizedBoundsChanged?.Invoke(this, e);

    private void ApplyMaximizedBounds()
    {
        var custom = WindowState == FormWindowState.Maximized && !_maximizedBounds.IsEmpty;
        if (_host is not null)
        {
            var frame = NonClient;
            _host.MaxWidth = custom ? Math.Max(1, _maximizedBounds.Width - frame.Horizontal)
                : MaximumSize.Width > 0 ? Math.Max(1, MaximumSize.Width - frame.Horizontal) : double.PositiveInfinity;
            _host.MaxHeight = custom ? Math.Max(1, _maximizedBounds.Height - frame.Vertical)
                : MaximumSize.Height > 0 ? Math.Max(1, MaximumSize.Height - frame.Vertical) : double.PositiveInfinity;
        }
        if (!custom) return;
        var area = GetTopLevel() ? Screen.GetWorkingArea(this) : Parent?.ClientRectangle ?? _maximizedBounds;
        var bounds = Rectangle.Intersect(area, _maximizedBounds);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Bounds = bounds;
    }

    private void ApplyFormAutoSize()
    {
        if (!AutoSize || _applyingAutoSize || WindowState != FormWindowState.Normal) return;
        var preferred = Padding.Size;
        foreach (Control child in Controls)
        {
            if (!((IArrangedElement)child).ParticipatesInLayout) continue;
            preferred.Width = Math.Max(preferred.Width, child.Right + child.Margin.Right + Padding.Right);
            preferred.Height = Math.Max(preferred.Height, child.Bottom + child.Margin.Bottom + Padding.Bottom);
        }
        if (AutoSizeMode == AutoSizeMode.GrowOnly)
            preferred = new Size(Math.Max(ClientSize.Width, preferred.Width), Math.Max(ClientSize.Height, preferred.Height));
        if (preferred == ClientSize) return;
        _applyingAutoSize = true;
        try
        {
            ClientSize = preferred;
        }
        finally
        {
            _applyingAutoSize = false;
        }
    }
}
