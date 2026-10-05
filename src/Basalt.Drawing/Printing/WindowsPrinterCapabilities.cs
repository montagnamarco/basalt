using System.Runtime.InteropServices;

namespace System.Drawing.Printing;

internal static class WindowsPrinterCapabilities
{
    private const ushort DcPapers = 2;
    private const ushort DcPaperSize = 3;
    private const ushort DcDuplex = 7;
    private const ushort DcEnumResolutions = 13;
    private const ushort DcPaperNames = 16;
    private const ushort DcColorDevice = 32;

    internal static PrinterCapabilities Discover(string printerName)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero)) return PrinterCapabilities.Unknown;
        try
        {
            // PRINTER_INFO_2 begins with server, printer, share, and port pointers.
            _ = GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);
            if (needed == 0) return PrinterCapabilities.Unknown;
            var info = Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if (!GetPrinter(printer, 2, info, needed, out _)) return PrinterCapabilities.Unknown;
                var port = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, 3 * IntPtr.Size));
                if (string.IsNullOrEmpty(port)) return PrinterCapabilities.Unknown;

                var duplex = BooleanCapability(printerName, port, DcDuplex);
                var color = BooleanCapability(printerName, port, DcColorDevice);
                return new PrinterCapabilities(duplex, color,
                    ReadPapers(printerName, port), ReadResolutions(printerName, port));
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    private static bool? BooleanCapability(string printer, string port, ushort capability)
    {
        var result = DeviceCapabilities(printer, port, capability, IntPtr.Zero, IntPtr.Zero);
        return result < 0 ? null : result != 0;
    }

    private static PaperSize[] ReadPapers(string printer, string port)
    {
        var count = DeviceCapabilities(printer, port, DcPapers, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0 || count > 1024) return [];

        var codes = ReadArray(printer, port, DcPapers, count, sizeof(short));
        var sizes = ReadArray(printer, port, DcPaperSize, count, sizeof(int) * 2);
        var names = ReadArray(printer, port, DcPaperNames, count, 64 * sizeof(char));
        if (codes == IntPtr.Zero || sizes == IntPtr.Zero || names == IntPtr.Zero)
        {
            Free(codes, sizes, names);
            return [];
        }

        try
        {
            var papers = new List<PaperSize>();
            for (var i = 0; i < count; i++)
            {
                var width = TenthsMillimeterToHundredthsInch(Marshal.ReadInt32(sizes, i * 8));
                var height = TenthsMillimeterToHundredthsInch(Marshal.ReadInt32(sizes, i * 8 + 4));
                if (width <= 0 || height <= 0) continue;
                var name = Marshal.PtrToStringUni(names + i * 128, 64)?.TrimEnd('\0') ?? string.Empty;
                papers.Add(new PaperSize(name, width, height)
                {
                    RawKind = unchecked((ushort)Marshal.ReadInt16(codes, i * 2)),
                });
            }
            return [.. papers];
        }
        finally
        {
            Free(codes, sizes, names);
        }
    }

    private static PrinterResolution[] ReadResolutions(string printer, string port)
    {
        var count = DeviceCapabilities(printer, port, DcEnumResolutions, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0 || count > 1024) return [];
        var buffer = ReadArray(printer, port, DcEnumResolutions, count, sizeof(int) * 2);
        if (buffer == IntPtr.Zero) return [];
        try
        {
            var resolutions = new List<PrinterResolution>();
            for (var i = 0; i < count; i++)
            {
                var x = Marshal.ReadInt32(buffer, i * 8);
                var y = Marshal.ReadInt32(buffer, i * 8 + 4);
                if (x > 0 && y > 0)
                    resolutions.Add(new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = x, Y = y });
            }
            return [.. resolutions];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr ReadArray(string printer, string port, ushort capability, int count, int itemSize)
    {
        var buffer = Marshal.AllocHGlobal(checked(count * itemSize));
        if (DeviceCapabilities(printer, port, capability, buffer, IntPtr.Zero) == count) return buffer;
        Marshal.FreeHGlobal(buffer);
        return IntPtr.Zero;
    }

    private static void Free(params IntPtr[] buffers)
    {
        foreach (var buffer in buffers)
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
    }

    internal static int TenthsMillimeterToHundredthsInch(int value) =>
        (int)Math.Round(value * 100d / 254d);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);

    [DllImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DeviceCapabilities(string printer, string port, ushort capability, IntPtr output, IntPtr mode);
}
