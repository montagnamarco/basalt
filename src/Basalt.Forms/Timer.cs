using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// Raises Tick on the UI thread at an interval, as the message loop
/// delivers WM_TIMER: ticks never overlap a handler and are skipped, not
/// queued, while the UI thread is busy.
/// </summary>
[DefaultProperty("Interval")]
[DefaultEvent("Tick")]
[ToolboxItemFilter("System.Windows.Forms")]
public class Timer : Component
{
    private int _interval = 100;
    private bool _enabled;
    private DispatcherTimer? _timer;

    public Timer()
    {
    }

    public Timer(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    [DefaultValue(false)]
    public virtual bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;

            _enabled = value;

            if (value) StartTimer();
            else StopTimer();
        }
    }

    /// <summary>Milliseconds between ticks; at least one.</summary>
    [DefaultValue(100)]
    public int Interval
    {
        get => _interval;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, $"Value '{value}' is not a valid value for Interval. Interval must be greater than 0.");

            if (value == _interval) return;

            _interval = value;
            if (_timer is not null) _timer.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    [Localizable(false)]
    [Bindable(true)]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    public event EventHandler? Tick;

    public void Start() => Enabled = true;

    public void Stop() => Enabled = false;

    protected virtual void OnTick(EventArgs e) => Tick?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        if (disposing) Enabled = false;

        base.Dispose(disposing);
    }

    public override string ToString() => $"{base.ToString()}, Interval: {Interval}";

    private void StartTimer()
    {
        AvaloniaPlatform.EnsureStarted();

        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(_interval), DispatcherPriority.Background, OnDispatcherTick);
        _timer.Interval = TimeSpan.FromMilliseconds(_interval);
        _timer.Start();
    }

    private void StopTimer() => _timer?.Stop();

    private void OnDispatcherTick(object? sender, EventArgs e)
    {
        try
        {
            OnTick(EventArgs.Empty);
        }
        catch (Exception exception) when (Application.HandlesThreadExceptions)
        {
            Application.OnThreadException(exception);
        }
    }
}
