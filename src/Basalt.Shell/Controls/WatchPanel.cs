using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>An expression the user is watching, and what it last evaluated to.</summary>
public sealed class WatchEntry
{
    public WatchEntry(string expression) => Expression = expression;

    public string Expression { get; }

    /// <summary>The last value, or null before it has been evaluated.</summary>
    public string? Value { get; private set; }

    /// <summary>Whether the expression could not be evaluated.</summary>
    public bool Failed { get; private set; }

    public string Display => Value is null
        ? $"{Expression} = …"
        : $"{Expression} = {Value}";

    public void Update(string? value)
    {
        // A null result means the debugger could not evaluate it: usually the
        // variable is not in scope in the frame being looked at, which is worth
        // showing rather than hiding as a blank.
        Failed = value is null;
        Value = value ?? "<not available>";
    }

    public void Reset()
    {
        Value = null;
        Failed = false;
    }
}

/// <summary>
/// Expressions the user wants to keep an eye on while stepping.
///
/// Everything is re-evaluated whenever execution stops, since a value from the
/// previous stop would be quietly wrong rather than merely stale.
/// </summary>
public sealed class WatchPanel : UserControl
{
    private readonly TextBox _input;
    private readonly ListBox _list;
    private readonly List<WatchEntry> _entries = [];

    public WatchPanel()
    {
        _input = new TextBox
        {
            PlaceholderText = "Add an expression",
            Margin = new Thickness(6, 6, 6, 4)
        };

        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            Add(_input.Text ?? "");
            _input.Text = "";
            e.Handled = true;
        };

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<WatchEntry>((entry, _) => new TextBlock
            {
                Text = entry?.Display,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = 12,
                Opacity = entry?.Failed == true ? 0.55 : 1
            })
        };

        _list.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete && e.Key != Key.Back) return;

            if (_list.SelectedItem is WatchEntry entry) Remove(entry);
            e.Handled = true;
        };

        var layout = new DockPanel();
        DockPanel.SetDock(_input, Avalonia.Controls.Dock.Top);
        layout.Children.Add(_input);
        layout.Children.Add(_list);

        Content = layout;
    }

    /// <summary>Raised when the set of watched expressions changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<WatchEntry> Entries => _entries;

    /// <summary>The box a watch expression is typed into, for tests.</summary>
    internal TextBox InputForTests => _input;

    /// <summary>The list of watches, for tests.</summary>
    internal ListBox ListForTests => _list;

    public void Add(string expression)
    {
        expression = expression.Trim();

        if (expression.Length == 0) return;
        if (_entries.Any(e => e.Expression == expression)) return;

        _entries.Add(new WatchEntry(expression));
        Refresh();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(WatchEntry entry)
    {
        if (!_entries.Remove(entry)) return;

        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-evaluates everything against the frame being looked at.
    ///
    /// The evaluator is asked one expression at a time because a failure on one
    /// says nothing about the others: an expression out of scope should show as
    /// unavailable while its neighbours still show their values.
    /// </summary>
    public async Task RefreshAsync(
        Func<string, Task<string?>> evaluate, CancellationToken ct = default)
    {
        foreach (var entry in _entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // ConfigureAwait(true): the display is updated below.
                entry.Update(await evaluate(entry.Expression).ConfigureAwait(true));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                entry.Update(null);
            }
        }

        Refresh();
    }

    /// <summary>Forgets the last values, which resuming makes meaningless.</summary>
    public void ClearValues()
    {
        foreach (var entry in _entries) entry.Reset();
        Refresh();
    }

    private void Refresh() => _list.ItemsSource = _entries.ToList();
}
