using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell;

/// <summary>
/// Asks what a method's parameters should become.
///
/// Reordering and removing are done on the list itself rather than by typing
/// a new signature: what a user wants is almost always to move one parameter
/// or drop one, and typing the whole thing again invites a typo that the
/// preview would then faithfully apply.
/// </summary>
public sealed class ChangeSignatureDialog : Window
{
    private readonly List<(int Index, string Name, string Type)> _parameters;
    private readonly List<(string Name, string Type, string DefaultArgument)> _added = [];
    private readonly StackPanel _rows;

    public ChangeSignatureDialog(IReadOnlyList<(string Name, string Type)> parameters)
    {
        Title = "Change Signature";
        Width = 460;
        Height = 380;

        // A floor, so resizing cannot hide the buttons.
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _parameters = [.. parameters.Select((p, i) => (i, p.Name, p.Type))];

        _rows = new StackPanel { Spacing = 2 };

        Fill();

        var add = new Button { Content = "Add a parameter" };
        add.Click += (_, _) => AddParameter();

        var ok = new Button { Content = "Apply", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        ok.Click += (_, _) =>
        {
            Result = new SignatureChange(
                [.. _parameters.Select(p => p.Index)],
                [.. _added]);

            Close();
        };

        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, ok }
        };

        var layout = new DockPanel { Margin = new Thickness(16) };

        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(add, Avalonia.Controls.Dock.Bottom);

        layout.Children.Add(buttons);
        layout.Children.Add(add);
        layout.Children.Add(new ScrollViewer { Content = _rows });

        Content = layout;

        // Focused when the dialog opens, so it can be confirmed or
        // tabbed away from without reaching for the mouse.
        Opened += (_, _) => ok.Focus();
    }

    /// <summary>What was asked for, or null if the user gave up.</summary>
    public SignatureChange? Result { get; private set; }

    /// <summary>Builds the rows again after something moved.</summary>
    private void Fill()
    {
        _rows.Children.Clear();

        foreach (var parameter in _parameters.ToList())
        {
            var captured = parameter;

            var label = new TextBlock
            {
                Text = $"{parameter.Name} As {parameter.Type}",
                VerticalAlignment = VerticalAlignment.Center
            };

            var up = new Button { Content = "↑", Padding = new Thickness(6, 0) };
            var down = new Button { Content = "↓", Padding = new Thickness(6, 0) };
            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0) };

            up.Click += (_, _) => Move(captured.Index, -1);
            down.Click += (_, _) => Move(captured.Index, 1);
            remove.Click += (_, _) => Remove(captured.Index);

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                Margin = new Thickness(0, 1)
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            Grid.SetColumn(remove, 3);

            row.Children.Add(label);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(remove);

            _rows.Children.Add(row);
        }

        foreach (var added in _added.ToList())
        {
            var captured = added;

            var label = new TextBlock
            {
                Text = $"{added.Name} As {added.Type}   (new, passing {added.DefaultArgument})",
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0) };

            remove.Click += (_, _) => { _added.Remove(captured); Fill(); };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(remove, 1);

            row.Children.Add(label);
            row.Children.Add(remove);

            _rows.Children.Add(row);
        }
    }

    private void Move(int index, int by)
    {
        var at = _parameters.FindIndex(p => p.Index == index);

        if (at < 0) return;

        var to = at + by;

        if (to < 0 || to >= _parameters.Count) return;

        (_parameters[at], _parameters[to]) = (_parameters[to], _parameters[at]);

        Fill();
    }

    private void Remove(int index)
    {
        _parameters.RemoveAll(p => p.Index == index);

        Fill();
    }

    /// <summary>
    /// Adds a parameter, asked for one field at a time.
    ///
    /// The argument to pass at the call sites is asked for too: without one
    /// every call would have to be fixed by hand, which is what this
    /// refactoring exists to avoid.
    /// </summary>
    private void AddParameter()
    {
        var name = new TextBox { PlaceholderText = "name", Width = 120 };
        var type = new TextBox { Text = "Integer", Width = 120 };
        var argument = new TextBox { Text = "0", Width = 120 };

        var confirm = new Button { Content = "Add" };

        confirm.Click += (_, _) =>
        {
            if (name.Text is { Length: > 0 } written)
            {
                _added.Add((written, type.Text ?? "Object", argument.Text ?? "Nothing"));
                Fill();
            }
        };

        _rows.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { name, type, new TextBlock
            {
                Text = "pass",
                VerticalAlignment = VerticalAlignment.Center
            }, argument, confirm }
        });
    }

    /// <summary>Moves a parameter, for tests.</summary>
    internal void MoveForTests(int index, int by) => Move(index, by);

    /// <summary>Removes a parameter, for tests.</summary>
    internal void RemoveForTests(int index) => Remove(index);

    /// <summary>The change as it stands, for tests.</summary>
    internal SignatureChange CurrentForTests() =>
        new([.. _parameters.Select(p => p.Index)], [.. _added]);
}
