using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Drawing.Printing;

internal static class SystemPrinterDiscovery
{
    internal readonly record struct Snapshot(string[] Names, string? DefaultName);

    /// <summary>
    /// Test-only seam: when set, <see cref="Discover"/> returns this instead of
    /// shelling out to the platform spooler. <see cref="PrinterSettings"/>
    /// caches whatever this returns, so tests can also prove the cache works
    /// by counting how many times their delegate actually runs.
    /// </summary>
    internal static Func<Snapshot>? DiscoveryOverride { get; set; }

    internal static Snapshot Discover() => DiscoveryOverride is not null
        ? DiscoveryOverride()
        : OperatingSystem.IsWindows() ? DiscoverWindows() : DiscoverCups();

    internal static string[] ParseCupsPrinters(string output)
    {
        var names = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var delimiter = trimmed.IndexOfAny([' ', '\t']);
            if (delimiter <= 0) continue;

            var name = trimmed[..delimiter];
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        return [.. names];
    }

    internal static string? ParseCupsDefault(string output)
    {
        const string prefix = "system default destination: ";
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal)) return trimmed[prefix.Length..].Trim();
        }

        return null;
    }

    private static Snapshot DiscoverCups()
    {
        var names = ParseCupsPrinters(RunLpstat("-a"));
        var defaultName = ParseCupsDefault(RunLpstat("-d"));
        return new Snapshot(names, defaultName);
    }

    private static string RunLpstat(string argument)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("lpstat", argument)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.Environment["LC_ALL"] = "C";
            process.Start();

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!output.Wait(TimeSpan.FromSeconds(3)))
            {
                process.Kill();
                return string.Empty;
            }

            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return string.Empty;
            }

            _ = errors.Result;
            return process.ExitCode == 0 ? output.Result : string.Empty;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return string.Empty;
        }
    }

    private static Snapshot DiscoverWindows()
    {
        const uint printerEnumLocal = 2;
        const uint printerEnumConnections = 4;
        _ = EnumPrinters(printerEnumLocal | printerEnumConnections, null, 4, IntPtr.Zero, 0, out var bytesNeeded, out _);
        if (bytesNeeded == 0) return new Snapshot([], GetDefaultWindowsPrinter());

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(printerEnumLocal | printerEnumConnections, null, 4, buffer, bytesNeeded, out _, out var count))
                return new Snapshot([], GetDefaultWindowsPrinter());

            var names = new List<string>();
            var size = Marshal.SizeOf<PrinterInfo4>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + i * size);
                if (!string.IsNullOrEmpty(info.Name) && !names.Contains(info.Name, StringComparer.Ordinal)) names.Add(info.Name);
            }

            return new Snapshot([.. names], GetDefaultWindowsPrinter());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetDefaultWindowsPrinter()
    {
        uint length = 0;
        _ = GetDefaultPrinter(null, ref length);
        if (length == 0) return null;

        var buffer = new StringBuilder(checked((int)length));
        return GetDefaultPrinter(buffer, ref length) ? buffer.ToString() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? buffer, ref uint size);
}
