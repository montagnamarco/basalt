namespace System.Windows.Forms;

/// <summary>
/// One step of a <see cref="NumericUpDown"/>'s acceleration: once its spin
/// buttons or arrow keys have been held this many seconds, <see cref="NumericUpDown.Increment"/>
/// is replaced by this <see cref="Increment"/> until a faster step applies or the key is released.
/// </summary>
public class NumericUpDownAcceleration
{
    public NumericUpDownAcceleration(int seconds, decimal increment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        ArgumentOutOfRangeException.ThrowIfNegative(increment);

        Seconds = seconds;
        Increment = increment;
    }

    public decimal Increment { get; set; }

    public int Seconds { get; set; }
}
