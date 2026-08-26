using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace Basalt.Razor.Vb.VisualStudio;

/// <summary>
/// Starts the language server for .vbhtml views and .vbpage pages.
///
/// The same server the VS Code extension and the Rider plugin use, which is
/// what keeps the three editors agreeing about what a view means.
/// </summary>
[ContentType(VbHtmlContentDefinition.ContentTypeName)]
[Export(typeof(ILanguageClient))]
public sealed class VbHtmlLanguageClient : ILanguageClient
{
    public string Name => "Razor for Visual Basic";

    public IEnumerable<string>? ConfigurationSections => null;

    public object? InitializationOptions => null;

    // Both, because the server answers both: watching only the view meant a
    // page edited outside the editor was never reported to the server.
    public IEnumerable<string>? FilesToWatch => new[] { "**/*.vbhtml", "**/*.vbpage", "**/*.vbrazor" };

    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;
    public event AsyncEventHandler<EventArgs>? StopAsync;

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();

        var server = FindServer();

        // Returning null leaves the file usable as plain text rather than
        // failing the whole editor session.
        if (server is null) return null;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(server)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        return process.Start()
            ? new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream)
            : null;
    }

    public async Task OnLoadedAsync()
    {
        if (StartAsync is { } start) await start.InvokeAsync(this, EventArgs.Empty);
    }

    public Task OnServerInitializeFailedAsync(Exception e) => Task.CompletedTask;

    public Task OnServerInitializedAsync() => Task.CompletedTask;

    /// <summary>The server shipped inside the extension.</summary>
    private static string? FindServer()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (directory is null) return null;

        var path = Path.Combine(directory, "server", "vbrazor-langserver.exe");

        return File.Exists(path) ? path : null;
    }
}
