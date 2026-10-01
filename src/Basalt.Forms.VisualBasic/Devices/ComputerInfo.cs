using System.Globalization;

namespace Microsoft.VisualBasic.Devices;

/// <summary>
/// The machine's operating system and memory, as My.Computer.Info exposes them
/// to a VB application.
/// </summary>
/// <remarks>
/// There is no single portable Win32 GlobalMemoryStatusEx equivalent across
/// Windows, Linux and macOS, so physical memory comes from
/// <see cref="GC.GetGCMemoryInfo"/>: the runtime already asks the OS for the
/// machine's total memory and its current load to size the garbage collector,
/// and that same answer serves this type. TotalAvailableMemoryBytes is refreshed
/// at process start and at every garbage collection, exactly as MemoryLoadBytes
/// is documented to be, so a reading between collections can lag reality
/// slightly; this is the same trade-off .NET's own GCMemoryInfo accepts.
/// Virtual memory has no equivalent on a 64-bit cross-platform runtime (no
/// 2 GB/4 GB process address-space ceiling to report), so those two members
/// report the same totals as physical memory rather than a guessed number.
/// </remarks>
public class ComputerInfo
{
    public ulong TotalPhysicalMemory => (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public ulong AvailablePhysicalMemory
    {
        get
        {
            var info = GC.GetGCMemoryInfo();
            var used = (ulong)info.MemoryLoadBytes;
            var total = (ulong)info.TotalAvailableMemoryBytes;

            return used < total ? total - used : 0;
        }
    }

    public ulong TotalVirtualMemory => TotalPhysicalMemory;

    public ulong AvailableVirtualMemory => AvailablePhysicalMemory;

    public CultureInfo InstalledUICulture => CultureInfo.InstalledUICulture;

    /// <summary>A human-readable description of the operating system, as RuntimeInformation.OSDescription reports it.</summary>
    public string OSFullName => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>The operating system's platform identifier (Win32NT on Windows, Unix elsewhere).</summary>
    public string OSPlatform => Environment.OSVersion.Platform.ToString();

    public string OSVersion => Environment.OSVersion.Version.ToString();
}
