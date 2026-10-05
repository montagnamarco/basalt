using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing.Printing;

/// <summary>Windows GDI spooler adapter. All native calls stay behind the Windows branch.</summary>
internal sealed class WindowsPrinterSpooler : IDisposable
{
    private const int RasterDpi = 300;
    private const int DmOutBuffer = 2;
    private const int DmInBuffer = 8;
    private readonly PrinterSettings _settings;
    private readonly string _documentName;
    private IntPtr _dc;
    private Bitmap? _bitmap;
    private bool _jobStarted;

    internal WindowsPrinterSpooler(PrinterSettings settings, string documentName)
    {
        _settings = settings;
        _documentName = documentName;
    }

    internal Graphics BeginPage(PrintPageEventArgs page)
    {
        ConfigurePage(page.PageSettings);
        if (!_jobStarted)
        {
            var info = new DocInfo { Size = Marshal.SizeOf<DocInfo>(), Name = _documentName };
            if (StartDoc(_dc, ref info) <= 0) throw LastWin32Error("StartDoc");
            _jobStarted = true;
        }

        if (StartPage(_dc) <= 0) throw LastWin32Error("StartPage");

        var bounds = page.PageBounds;
        var width = PixelsAtDpi(bounds.Width, RasterDpi);
        var height = PixelsAtDpi(bounds.Height, RasterDpi);
        _bitmap = new Bitmap(width, height);
        return CreateRasterGraphics(_bitmap);
    }

    internal static Graphics CreateRasterGraphics(Bitmap bitmap)
    {
        var canvas = new SKCanvas(bitmap.Pixels);
        canvas.Clear(SKColors.White);
        var graphics = new Graphics(canvas, ownsCanvas: true, bitmap) { Resolution = RasterDpi, IsPrinter = true };
        bitmap.MarkInUse();
        graphics.PageUnit = GraphicsUnit.Display;
        return graphics;
    }

    internal void EndPage(PrintPageEventArgs page)
    {
        page.Graphics.Dispose();
        var bitmap = _bitmap ?? throw new InvalidOperationException("No printer page is active.");
        try
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var info = new BitmapInfo
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            };
            var offsetX = GetDeviceCaps(_dc, 112);
            var offsetY = GetDeviceCaps(_dc, 113);
            var physicalWidth = GetDeviceCaps(_dc, 110);
            var physicalHeight = GetDeviceCaps(_dc, 111);
            if (physicalWidth <= 0 || physicalHeight <= 0) throw new IOException("The printer reported an invalid paper size.");

            var result = StretchDIBits(_dc, -offsetX, -offsetY, physicalWidth, physicalHeight,
                0, 0, width, height, bitmap.Pixels.GetPixels(), ref info, 0, 0x00CC0020);
            if (result == 0 || result == -1) throw LastWin32Error("StretchDIBits");
            if (EndPageNative(_dc) <= 0) throw LastWin32Error("EndPage");
        }
        finally
        {
            bitmap.Dispose();
            _bitmap = null;
        }
    }

    internal void End(bool cancel)
    {
        try
        {
            if (_jobStarted)
            {
                var result = cancel ? AbortDoc(_dc) : EndDoc(_dc);
                if (result <= 0 && !cancel) throw LastWin32Error("EndDoc");
            }
        }
        finally
        {
            _jobStarted = false;
            Dispose();
        }
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (_dc != IntPtr.Zero) DeleteDC(_dc);
        _dc = IntPtr.Zero;
    }

    internal static int PixelsAtDpi(int hundredthsOfInch, int dpi) =>
        Math.Max(1, checked((int)Math.Round(hundredthsOfInch * dpi / 100d)));

    private void ConfigurePage(PageSettings page)
    {
        if (!OpenPrinter(_settings.PrinterName, out var printer, IntPtr.Zero)) throw LastWin32Error("OpenPrinter");
        try
        {
            var size = DocumentProperties(IntPtr.Zero, printer, _settings.PrinterName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0) throw LastWin32Error("DocumentProperties");

            var mode = Marshal.AllocHGlobal(size);
            try
            {
                if (DocumentProperties(IntPtr.Zero, printer, _settings.PrinterName, mode, IntPtr.Zero, DmOutBuffer) != 1)
                    throw LastWin32Error("DocumentProperties");

                ApplyPageMode(mode, page, _settings);
                if (DocumentProperties(IntPtr.Zero, printer, _settings.PrinterName, mode, mode, DmInBuffer | DmOutBuffer) != 1)
                    throw LastWin32Error("DocumentProperties");

                var configuredDc = _dc == IntPtr.Zero
                    ? CreateDC("WINSPOOL", _settings.PrinterName, null, mode)
                    : ResetDC(_dc, mode);
                if (configuredDc == IntPtr.Zero) throw LastWin32Error("CreateDC/ResetDC");
                _dc = configuredDc;
            }
            finally
            {
                Marshal.FreeHGlobal(mode);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    // Byte offsets into Win32's DEVMODEW, counted from the start of the
    // struct: dmDeviceName is a fixed WCHAR[32] (64 bytes, offset 0), so the
    // named fields below start at offset 64. The layout is part of the
    // Win32 ABI and never changes, so fixed offsets (rather than
    // Marshal.OffsetOf on a declared struct) are safe and avoid marshaling a
    // structure that is mostly unused here.
    internal const int DmSizeOffset = 68; // dmSize (WORD): total struct length the driver filled in.
    internal const int DmFieldsOffset = 72; // dmFields (DWORD): bitmask of which union/trailing fields are set.
    internal const int DmOrientationOffset = 76; // dmOrientation (short): DMORIENT_PORTRAIT (1) or DMORIENT_LANDSCAPE (2).
    internal const int DmPaperSizeOffset = 78; // dmPaperSize (short): a DMPAPER_* constant, or 0 for a custom size.
    internal const int DmPaperLengthOffset = 80; // dmPaperLength (short): custom paper height, tenths of a millimeter.
    internal const int DmPaperWidthOffset = 82; // dmPaperWidth (short): custom paper width, tenths of a millimeter.
    internal const int DmCopiesOffset = 86; // dmCopies (short): number of copies, when DM_COPIES is set.
    internal const int DmColorOffset = 92; // dmColor (short): DMCOLOR_MONOCHROME (1) or DMCOLOR_COLOR (2).
    internal const int DmDuplexOffset = 94; // dmDuplex (short): DMDUP_SIMPLEX/VERTICAL/HORIZONTAL.
    internal const int DmCollateOffset = 100; // dmCollate (short): DMCOLLATE_FALSE (0) or DMCOLLATE_TRUE (1).

    private const uint DmOrientation = 0x00000001;
    private const uint DmPaperSize = 0x00000002;
    private const uint DmPaperLength = 0x00000004;
    private const uint DmPaperWidth = 0x00000008;
    private const uint DmCopies = 0x00000100;
    private const uint DmColor = 0x00000800;
    private const uint DmDuplex = 0x00001000;
    private const uint DmCollate = 0x00008000;

    internal static void ApplyPageMode(IntPtr mode, PageSettings page, PrinterSettings settings)
    {
        if (Marshal.ReadInt16(mode, DmSizeOffset) < 102) throw new IOException("The printer returned a short DEVMODE structure.");

        var fields = unchecked((uint)Marshal.ReadInt32(mode, DmFieldsOffset));
        fields |= DmOrientation;
        Marshal.WriteInt16(mode, DmOrientationOffset, (short)(page.Landscape ? 2 : 1));

        if (page.PaperSize.RawKind > 0)
        {
            fields |= DmPaperSize;
            fields &= ~(DmPaperLength | DmPaperWidth);
            Marshal.WriteInt16(mode, DmPaperSizeOffset, checked((short)page.PaperSize.RawKind));
        }
        else
        {
            fields |= DmPaperLength | DmPaperWidth;
            fields &= ~DmPaperSize;
            Marshal.WriteInt16(mode, DmPaperLengthOffset, checked((short)Math.Round(page.PaperSize.Height * 2.54)));
            Marshal.WriteInt16(mode, DmPaperWidthOffset, checked((short)Math.Round(page.PaperSize.Width * 2.54)));
        }

        fields |= DmCopies | DmColor;
        Marshal.WriteInt16(mode, DmCopiesOffset, Math.Max((short)1, settings.Copies));
        Marshal.WriteInt16(mode, DmColorOffset, (short)(page.Color ? 2 : 1));
        if (settings.Duplex != Duplex.Default)
        {
            fields |= DmDuplex;
            Marshal.WriteInt16(mode, DmDuplexOffset, (short)settings.Duplex);
        }

        fields |= DmCollate;
        Marshal.WriteInt16(mode, DmCollateOffset, (short)(settings.Collate ? 1 : 0));
        Marshal.WriteInt32(mode, DmFieldsOffset, unchecked((int)fields));
    }

    private static IOException LastWin32Error(string operation) =>
        new($"Windows printer operation {operation} failed: {Marshal.GetLastWin32Error()}.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        public IntPtr Output;
        public IntPtr DataType;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(IntPtr window, IntPtr printer, string name, IntPtr output, IntPtr input, int mode);
    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr mode);
    [DllImport("gdi32.dll", EntryPoint = "ResetDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ResetDC(IntPtr dc, IntPtr mode);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDoc(IntPtr dc, ref DocInfo info);
    [DllImport("gdi32.dll", EntryPoint = "StartPage", SetLastError = true)]
    private static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "EndPage", SetLastError = true)]
    private static extern int EndPageNative(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "EndDoc", SetLastError = true)]
    private static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "AbortDoc", SetLastError = true)]
    private static extern int AbortDoc(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "GetDeviceCaps")]
    private static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("gdi32.dll", EntryPoint = "StretchDIBits", SetLastError = true)]
    private static extern int StretchDIBits(IntPtr dc, int x, int y, int destWidth, int destHeight,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, IntPtr bits, ref BitmapInfo info, uint usage, uint operation);
}
