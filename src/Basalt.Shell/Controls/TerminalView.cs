using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Workspace.Terminal;

namespace Basalt.Shell.Controls;

/// <summary>
/// A terminal panel backed by a real pseudo-terminal.
///
/// Keystrokes go straight to the child process rather than through a separate
/// input box: that is what lets the shell draw its own prompt, complete with
/// Tab, recall history with the arrow keys, and run full-screen programs.
/// </summary>
public sealed class TerminalView : UserControl, IDisposable
{
    private readonly IPtyConnection _pty;
    private readonly AnsiScreen _screen = new();
    private readonly TextBox _display;
    private readonly ScrollViewer _scroller;
    private bool _disposed;

    /// <summary>
    /// Opens a terminal.
    ///
    /// An empty <paramref name="shell"/> uses the user's login shell, which is
    /// what they expect from a terminal they did not configure.
    /// </summary>
    public TerminalView(string workingDirectory, string shell = "")
    {
        WorkingDirectory = workingDirectory;
        Shell = shell;

        _display = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonospaceFont,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            // The caret belongs to the shell, not to this read-only view.
            CaretBrush = Brushes.Transparent
        };

        _scroller = new ScrollViewer
        {
            Content = _display,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Content = _scroller;
        Focusable = true;

        BuildContextMenu();

        _screen.Changed += OnScreenChanged;

        try
        {
            _pty = CreateConnection(workingDirectory, shell);
        }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException
                                       or System.ComponentModel.Win32Exception)
        {
            // Said in the panel rather than thrown from a constructor on the
            // interface thread, which took the whole IDE down.
            _pty = new UnavailablePtyConnection(ex.Message);
            _screen.Append(Localizer.Get(StringKeys.TerminalCouldNotStart, ex.Message));
        }

        _pty.OutputReceived += OnOutputReceived;
        _pty.Exited += OnExited;

        // Typing anywhere in the panel goes to the terminal.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        PointerPressed += (_, _) => Focus();
    }

    private static FontFamily MonospaceFont => new("Menlo,Consolas,DejaVu Sans Mono,monospace");

    public string WorkingDirectory { get; }

    /// <summary>The shell this terminal was started with, empty for the default.</summary>
    public string Shell { get; } = "";

    /// <summary>Tab caption, taken from the working directory.</summary>
    public string Title => Path.GetFileName(
        WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>What the terminal's menu can ask for.</summary>
    public enum TerminalCommand { Copy, Paste, Clear, Close }

    /// <summary>Raised when the user picks something from the menu.</summary>
    public event EventHandler<TerminalCommand>? CommandChosen;

    internal ContextMenu? Menu => _display.ContextMenu;

    /// <summary>
    /// Builds the menu.
    ///
    /// Copying needs a selection; the rest always apply, since a terminal can
    /// always be cleared or closed.
    /// </summary>
    private void BuildContextMenu()
    {
        void Choose(TerminalCommand command) => CommandChosen?.Invoke(this, command);

        _display.ContextMenu = ContextMenus.Build(
        [
            new("Copy", () => Choose(TerminalCommand.Copy))
                { IsAvailable = () => _display.SelectionEnd > _display.SelectionStart },
            new("Paste", () => Choose(TerminalCommand.Paste)),
            MenuAction.Separator,
            new("Clear", () => Choose(TerminalCommand.Clear)),
            MenuAction.Separator,
            new("Close Terminal", () => Choose(TerminalCommand.Close))
        ]);
    }

    public bool IsRunning => _pty.IsRunning;

    /// <summary>Text currently shown, escape sequences already interpreted.</summary>
    public string ScreenText => _screen.Text;

    /// <summary>
    /// Starts the shell.
    ///
    /// A configured shell that does not exist falls back to the default rather
    /// than failing: a typo in a setting should not leave the user without a
    /// terminal at all.
    /// </summary>
    private static IPtyConnection CreateConnection(string workingDirectory, string shell)
    {
        var chosen = shell.Length > 0 && File.Exists(shell) ? shell : DefaultShell();

        if (OperatingSystem.IsWindows())
        {
            // cmd.exe, PowerShell and pwsh do not understand "-i": on Windows
            // a console session is interactive by default, and cmd.exe treats
            // an unknown switch as the name of a file to run.
            return new WindowsPtyConnection(chosen, [], workingDirectory);
        }

        // An interactive login shell prints a prompt and reads its startup
        // files, which is what makes the panel behave like a real terminal.
        return new UnixPtyConnection(chosen, ["-i"], workingDirectory);
    }

    /// <summary>The user's configured shell, falling back to a POSIX default.</summary>
    public static string DefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

        var shell = Environment.GetEnvironmentVariable("SHELL");
        return string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell;
    }

    private void OnOutputReceived(object? sender, string text) => _screen.Append(text);

    private void OnScreenChanged(object? sender, EventArgs e)
    {
        // Output arrives on the reader thread.
        Dispatcher.UIThread.Post(() =>
        {
            _display.Text = _screen.Text;
            _display.CaretIndex = _display.Text?.Length ?? 0;
            _scroller.ScrollToEnd();
        });
    }

    private void OnExited(object? sender, int exitCode) =>
        _screen.Append($"\n[process exited with code {exitCode}]\n");

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        _pty.Write(e.Text);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl and Alt combinations map to the control bytes a terminal sends.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is >= Key.A and <= Key.Z)
        {
            var control = (char)(e.Key - Key.A + 1);
            _pty.Write(control.ToString());
            e.Handled = true;
            return;
        }

        var sequence = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\u007f",
            Key.Tab => "\t",
            Key.Escape => "\u001b",
            // Arrow keys and friends use the escape sequences the shell expects
            // for history recall and line editing.
            Key.Up => "\u001b[A",
            Key.Down => "\u001b[B",
            Key.Right => "\u001b[C",
            Key.Left => "\u001b[D",
            Key.Home => "\u001b[H",
            Key.End => "\u001b[F",
            Key.Delete => "\u001b[3~",
            Key.PageUp => "\u001b[5~",
            Key.PageDown => "\u001b[6~",
            _ => null
        };

        if (sequence is null) return;

        _pty.Write(sequence);
        e.Handled = true;
    }

    /// <summary>Gives the terminal keyboard focus.</summary>
    public void FocusInput() => Focus();

    public void Clear() => _screen.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pty.OutputReceived -= OnOutputReceived;
        _pty.Exited -= OnExited;
        _screen.Changed -= OnScreenChanged;
        _pty.Dispose();
    }

    /// <summary>
    /// Applies the user's settings to this terminal.
    ///
    /// The shell is not changed on a running session: the terminal the user is
    /// working in would have to be killed, which is not what changing a
    /// setting should do. New terminals pick it up.
    /// </summary>
    public void ApplySettings(Basalt.Core.Settings.IdeSettings settings)
    {
        _display.FontSize = settings.Terminal.FontSize;
    }
}
