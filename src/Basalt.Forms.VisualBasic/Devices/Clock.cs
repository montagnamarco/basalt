namespace Microsoft.VisualBasic.Devices;

/// <summary>The machine's clock, as My.Computer.Clock exposes it to a VB application.</summary>
public class Clock
{
    /// <summary>The current date and time, in the local time zone.</summary>
    public DateTime LocalTime => DateTime.Now;

    /// <summary>The current date and time, in Coordinated Universal Time.</summary>
    public DateTime GmtTime => DateTime.UtcNow;

    /// <summary>Milliseconds since the machine started, wrapping every ~24.8 days as Environment.TickCount does.</summary>
    public int TickCount => Environment.TickCount;
}
