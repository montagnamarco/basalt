using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>
/// The rows and columns of the grid being designed.
/// </summary>
/// <remarks>
/// Laying a form out by cells is only worth offering if the cells can be
/// defined here: without this the designer can put a control in row 2 but
/// only a text editor can say that row 2 exists, which sends the user to the
/// XAML for the one decision the designer is for.
///
/// Sizes are typed rather than picked from a list. "Auto", "*", "2*" and a
/// number are all valid and mean different things, and a list of the first
/// two would quietly rule out the rest.
/// </remarks>
public sealed class GridTrackEditor : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = Spacing.Tight };
    private readonly StackPanel _columns = new() { Spacing = Spacing.Tight };
    private readonly TextBlock _heading;

    private DesignerSession? _session;

    /// <summary>Raised when the grid changed, so the surface can redraw.</summary>
    public event EventHandler? Changed;

    public GridTrackEditor()
    {
        _heading = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, Spacing.Tight),
        };

        Content = new StackPanel
        {
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Spacing = Spacing.Normal,
            Children =
            {
                _heading,
                Section(Localizer.Get(StringKeys.DesignerRows), _rows, DesignerLayout.Track.Row),
                Section(Localizer.Get(StringKeys.DesignerColumns), _columns, DesignerLayout.Track.Column),
            },
        };
    }

    /// <summary>Whether there is a grid to edit; the panel hides when there is not.</summary>
    public bool HasGrid => _session?.GridInScope is not null;

    /// <summary>The rows as shown, for tests.</summary>
    internal IReadOnlyList<string> Rows => Sizes(_rows);

    /// <summary>The columns as shown, for tests.</summary>
    internal IReadOnlyList<string> Columns => Sizes(_columns);

    /// <summary>Shows the rows and columns of whatever the session has in scope.</summary>
    public void Show(DesignerSession? session)
    {
        _session = session;

        IsVisible = HasGrid;

        if (!IsVisible) return;

        // The panel above already names the selected element, so repeating
        // "Grid" here said the same word twice. Only the case that is not
        // obvious from it is worth a line.
        var ownGrid = session!.Selection?.Name.LocalName == "Grid";

        _heading.Text = ownGrid ? "" : Localizer.Get(StringKeys.DesignerContainingGrid);
        _heading.IsVisible = !ownGrid;

        Fill(_rows, session.TracksOf(DesignerLayout.Track.Row), DesignerLayout.Track.Row);
        Fill(_columns, session.TracksOf(DesignerLayout.Track.Column), DesignerLayout.Track.Column);
    }

    private Control Section(string title, StackPanel list, DesignerLayout.Track track)
    {
        var add = new Button
        {
            Content = "+",
            FontSize = 12,
            Padding = new Thickness(Spacing.Normal, 0),
            Margin = new Thickness(16 + Spacing.Tight, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = default,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        ToolTip.SetTip(add, Localizer.Get(track == DesignerLayout.Track.Row
                ? StringKeys.DesignerAddRow
                : StringKeys.DesignerAddColumn));

        add.Click += (_, _) =>
        {
            _session?.AddTrack(track);
            Show(_session);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        return new StackPanel
        {
            Spacing = Spacing.Tight,
            Children =
            {
                new TextBlock { Text = title, FontSize = 11, Opacity = 0.7 },
                list,
                add,
            },
        };
    }

    /// <summary>Draws one row per track, each with its size and a way out.</summary>
    private void Fill(StackPanel list, IReadOnlyList<string> tracks, DesignerLayout.Track track)
    {
        list.Children.Clear();

        // A grid with nothing declared has one implicit track. Shown as the
        // "*" it behaves as, so that the panel is never blank in front of a
        // grid that plainly has a row in it.
        var shown = tracks.Count == 0 ? ["*"] : tracks;

        for (var i = 0; i < shown.Count; i++)
        {
            var index = i;

            var size = new TextBox
            {
                Text = shown[i],
                FontSize = 11,
                Width = 70,
                Padding = new Thickness(Spacing.Tight, 1),
            };

            // On losing focus rather than on every keystroke: rewriting the
            // grid halfway through typing "2*" would apply "2" first and move
            // every control in it.
            size.LostFocus += (_, _) => Commit(track);

            var remove = new Button
            {
                Content = "×",
                FontSize = 12,
                Padding = new Thickness(Spacing.Normal, 0),
                Background = Brushes.Transparent,
                BorderThickness = default,
                // The last one cannot go: a grid has at least one of each.
                IsEnabled = shown.Count > 1,
            };

            remove.Click += (_, _) =>
            {
                _session?.RemoveTrack(track, index);
                Show(_session);
                Changed?.Invoke(this, EventArgs.Empty);
            };

            list.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Spacing.Tight,
                Children =
                {
                    new TextBlock
                    {
                        Text = index.ToString(),
                        FontSize = 11,
                        Width = 16,
                        Opacity = 0.6,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    size,
                    remove,
                },
            });
        }
    }

    /// <summary>Writes what the boxes now say back to the document.</summary>
    private void Commit(DesignerLayout.Track track)
    {
        if (_session is null) return;

        var list = track == DesignerLayout.Track.Row ? _rows : _columns;
        var sizes = Sizes(list);

        if (sizes.SequenceEqual(_session.TracksOf(track))) return;

        _session.SetTracks(track, sizes);

        Show(_session);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> Sizes(StackPanel list) =>
    [
        .. list.Children
            .OfType<StackPanel>()
            .Select(row => row.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? "*")
    ];

    /// <summary>Adds a track, as the button does. For tests.</summary>
    internal void AddForTests(DesignerLayout.Track track)
    {
        _session?.AddTrack(track);
        Show(_session);
    }

    /// <summary>Removes a track, as the button does. For tests.</summary>
    internal void RemoveForTests(DesignerLayout.Track track, int index)
    {
        _session?.RemoveTrack(track, index);
        Show(_session);
    }
}
