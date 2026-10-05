using Avalonia.Input;
using Avalonia.Input.Platform;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Bridges WinForms' synchronous clipboard calls to Avalonia's data transfer.</summary>
internal static class PlatformClipboard
{
    private static DataObject s_fallback = new();
    private static IAsyncDataTransfer? s_ownedTransfer;
    private static IClipboardAccess? s_testClipboard;

    internal static int Revision { get; private set; }

    internal static void UseClipboardForTests(IClipboardAccess? clipboard)
    {
        s_testClipboard = clipboard;
        s_ownedTransfer = null;
        s_fallback = new DataObject();
        Revision++;
    }

    public static void SetText(string text)
    {
        var data = new DataObject();
        data.SetText(text);
        SetData(data);
    }

    public static string? GetText() => ReadData()?.GetText() ?? s_fallback.GetText();

    public static void SetData(DataObject data)
    {
        s_fallback = data;
        Revision++;

        if (Clipboard() is not { } clipboard) return;

        var host = Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(window => window is not null);
        var transfer = ClipboardDataTransfer.Export(data, host?.StorageProvider);
        SynchronousWait.For(clipboard.SetDataAsync(transfer));
        s_ownedTransfer = transfer;
    }

    /// <returns>The current clipboard data. An owned transfer preserves its live managed values.</returns>
    public static DataObject? ReadData()
    {
        if (Clipboard() is not { } clipboard) return s_fallback;

        var owned = SynchronousWait.For(clipboard.TryGetInProcessDataAsync());
        if (owned is not null && ReferenceEquals(owned, s_ownedTransfer)) return s_fallback;

        using var transfer = SynchronousWait.For(clipboard.TryGetDataAsync());
        return transfer is null ? new DataObject() : ClipboardDataTransfer.Import(transfer);
    }

    public static void Clear()
    {
        s_fallback = new DataObject();
        s_ownedTransfer = null;
        Revision++;
        if (Clipboard() is { } clipboard) SynchronousWait.For(clipboard.ClearAsync());
    }

    private static IClipboardAccess? Clipboard()
    {
        if (s_testClipboard is not null) return s_testClipboard;

        var host = Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(window => window is not null);
        if (host is null) return null;

        try { return host.Clipboard is { } clipboard ? new AvaloniaClipboardAccess(clipboard) : null; }
        catch (InvalidOperationException) { return null; }
    }

    private sealed class AvaloniaClipboardAccess(IClipboard clipboard) : IClipboardAccess
    {
        public Task SetDataAsync(IAsyncDataTransfer? data) => clipboard.SetDataAsync(data);
        public Task<IAsyncDataTransfer?> TryGetDataAsync() => clipboard.TryGetDataAsync();
        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => clipboard.TryGetInProcessDataAsync();
        public Task ClearAsync() => clipboard.ClearAsync();
    }
}

internal interface IClipboardAccess
{
    Task SetDataAsync(IAsyncDataTransfer? data);
    Task<IAsyncDataTransfer?> TryGetDataAsync();
    Task<IAsyncDataTransfer?> TryGetInProcessDataAsync();
    Task ClearAsync();
}
