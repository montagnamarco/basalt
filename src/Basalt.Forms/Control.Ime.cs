using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The input method editor mode. There is no Windows IME context to switch
/// here: the platform's own input method composes text and Basalt receives
/// the result, so the mode is a value kept with WinForms' inheritance rules
/// for code that reads it back, raising ImeModeChanged like the real one.
/// </summary>
public partial class Control
{
    private static readonly object s_imeModeChangedEvent = new();

    private ImeMode? _imeMode;

    /// <summary>OnHalf reads back as On, as in WinForms, where the two only differ for the Korean IME.</summary>
    [Localizable(true)]
    [AmbientValue(ImeMode.Inherit)]
    public ImeMode ImeMode
    {
        get => ImeModeBase is ImeMode.OnHalf ? ImeMode.On : ImeModeBase;
        set => ImeModeBase = value;
    }

    /// <summary>The stored mode, Inherit resolving to the parent's mode and, at the top, NoControl.</summary>
    protected virtual ImeMode ImeModeBase
    {
        get
        {
            var mode = _imeMode ?? DefaultImeMode;
            if (mode != ImeMode.Inherit) return mode;

            return _parent?.ImeModeBase ?? ImeMode.NoControl;
        }
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ImeMode));

            var old = ImeModeBase;
            _imeMode = value;

            if (old != ImeModeBase) OnImeModeChanged(EventArgs.Empty);
        }
    }

    protected virtual ImeMode DefaultImeMode => ImeMode.Inherit;

    /// <summary>A control whose default is Disable, such as a button, never takes text from an IME.</summary>
    protected virtual bool CanEnableIme => DefaultImeMode != ImeMode.Disable;

    /// <summary>The mode carried from one control to the next on focus changes; without a Windows IME it is never set.</summary>
    protected static ImeMode PropagatingImeMode => ImeMode.Inherit;

    public event EventHandler? ImeModeChanged
    {
        add => Events.AddHandler(s_imeModeChangedEvent, value);
        remove => Events.RemoveHandler(s_imeModeChangedEvent, value);
    }

    protected virtual void OnImeModeChanged(EventArgs e) => ((EventHandler?)Events[s_imeModeChangedEvent])?.Invoke(this, e);

    public void ResetImeMode() => ImeMode = DefaultImeMode;

    internal bool ShouldSerializeImeMode() => _imeMode is { } mode && mode != DefaultImeMode;
}
