using System.Drawing;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using DrawingColor = System.Drawing.Color;
using MediaColor = Avalonia.Media.Color;

namespace Basalt.Shell.Controls;

/// <summary>A draft color picker; only the affirmative close exposes a value to the document owner.</summary>
internal sealed class WinFormsColorEditor : Window
{
    private readonly ComboBox _kind = new() { Name = "ColorKind" };
    private readonly ListBox _names = new() { Name = "ColorNames", Height = 180 };
    private readonly TextBox _hex = new() { Name = "ColorHex" };
    private readonly Border _preview = new() { Name = "ColorPreview", Height = 36 };
    private readonly TextBlock _error = new() { Name = "ColorError" };
    private readonly Button _ok;
    private readonly DrawingColor _initial;
    private readonly Dictionary<int, string?> _selectedNames = new();
    private int _activeKind = -1;
    private bool _customVisited;
    private bool _updating;
    private DrawingColor? _draft;

    public DrawingColor? SelectedColor { get; private set; }

    public WinFormsColorEditor(DrawingColor initial)
    {
        _initial = initial;
        Title = Localizer.Get(StringKeys.WinFormsColorEditorTitle);
        Width = 360;
        Height = 430;
        MinWidth = 300;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _kind.ItemsSource = new[]
        {
            Localizer.Get(StringKeys.WinFormsColorEditorKnown),
            Localizer.Get(StringKeys.WinFormsColorEditorSystem),
            Localizer.Get(StringKeys.WinFormsColorEditorCustom),
        };
        _ok = MakeButton("ColorOk", "WinFormsEditor_Ok", Save);
        _ok.IsDefault = true;
        var cancel = MakeButton("ColorCancel", "WinFormsEditor_Cancel", () => Close(false));
        cancel.IsCancel = true;
        _hex.Text = FormatHex(initial);
        _kind.SelectionChanged += (_, _) => ChangeKind();
        _names.SelectionChanged += (_, _) => UpdateDraft();
        // TextChanged is posted by Avalonia; the immediate property notification also
        // covers typing followed by an OK click within the same input turn.
        _hex.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                UpdateDraft();
            }
        };

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _kind,
                _names,
                new TextBlock { Text = Localizer.Get(StringKeys.WinFormsColorEditorHex) },
                _hex,
                new TextBlock { Text = Localizer.Get(StringKeys.WinFormsColorEditorPreview) },
                _preview,
                _error,
            },
        };
        Content = WinFormsEditorDialogLayout.Create(body, _ok, cancel);
        if (initial.IsSystemColor)
        {
            _kind.SelectedIndex = 1;
        }
        else if (initial.IsNamedColor)
        {
            _kind.SelectedIndex = 0;
        }
        else
        {
            _kind.SelectedIndex = 2;
        }
    }

    private static Button MakeButton(string name, string key, Action action)
    {
        var button = new Button
        {
            Name = name,
            Content = Localizer.Get(key),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ChangeKind()
    {
        if (_activeKind is 0 or 1)
        {
            _selectedNames[_activeKind] = _names.SelectedItem as string;
        }
        var nextKind = _kind.SelectedIndex;
        if (nextKind == 2 && !_customVisited)
        {
            _hex.Text = FormatHex(_draft ?? _initial);
            _customVisited = true;
        }
        _updating = true;
        var custom = nextKind == 2;
        _names.IsVisible = !custom;
        _hex.IsVisible = custom;
        _names.ItemsSource = nextKind == 1 ? SystemNames : KnownNames;
        var initialCategory = _initial.IsSystemColor ? 1 : 0;
        _names.SelectedItem = _selectedNames.TryGetValue(nextKind, out var remembered)
            ? remembered
            : _initial.IsNamedColor && nextKind == initialCategory ? _initial.Name : null;
        if (!custom && _names.SelectedItem is null)
        {
            _names.SelectedIndex = 0;
        }
        _activeKind = nextKind;

        _updating = false;
        UpdateDraft();
    }

    private void UpdateDraft()
    {
        if (_updating)
        {
            return;
        }

        _draft = _kind.SelectedIndex switch
        {
            0 or 1 when _names.SelectedItem is string name => DrawingColor.FromName(name),
            2 when TryParseHex(_hex.Text, out var color) => color,
            _ => null,
        };
        _ok.IsEnabled = _draft.HasValue;
        _error.Text = _draft.HasValue ? "" : Localizer.Get(StringKeys.WinFormsEditorInvalidColor);
        _preview.Background = _draft is { } preview
            ? new SolidColorBrush(MediaColor.FromArgb(preview.A, preview.R, preview.G, preview.B))
            : null;
    }

    private void Save()
    {
        UpdateDraft();
        if (_draft is not { } value)
        {
            return;
        }

        SelectedColor = value;
        Close(true);
    }

    internal static string FormatHex(DrawingColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static bool TryParseHex(string? text, out DrawingColor color)
    {
        color = default;
        var hex = text?.Trim();
        if (hex?.StartsWith('#') == true)
        {
            hex = hex[1..];
        }
        if (hex is null || hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        if (hex.Length == 6)
        {
            value |= 0xFF000000;
        }

        color = DrawingColor.FromArgb(unchecked((int)value));
        return true;
    }

    private static readonly string[] KnownNames = Enum.GetValues<KnownColor>()
        .Select(DrawingColor.FromKnownColor)
        .Where(color => !color.IsSystemColor)
        .Select(color => color.Name)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static readonly string[] SystemNames = Enum.GetValues<KnownColor>()
        .Select(DrawingColor.FromKnownColor)
        .Where(color => color.IsSystemColor)
        .Select(color => color.Name)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
}
