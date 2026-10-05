using System.Diagnostics.CodeAnalysis;
using System.Drawing.Text;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;

namespace Basalt.Shell.Controls;

/// <summary>A modal font draft that preserves the original unit, charset, and vertical flag.</summary>
internal sealed class WinFormsFontEditor : Window
{
    private readonly TextBox _family = new() { Name = "FontFamily" };
    private readonly TextBox _size = new() { Name = "FontSize" };
    private readonly ListBox _families = new() { Name = "FontFamilies", Height = 120 };
    private readonly CheckBox _bold = new() { Name = "FontBold" };
    private readonly CheckBox _italic = new() { Name = "FontItalic" };
    private readonly CheckBox _underline = new() { Name = "FontUnderline" };
    private readonly CheckBox _strikeout = new() { Name = "FontStrikeout" };
    private readonly TextBlock _preview = new() { Name = "FontPreview" };
    private readonly TextBlock _error = new() { Name = "FontError" };
    private readonly Button _ok;
    private readonly DrawingGraphicsUnit _unit;
    private readonly byte _characterSet;
    private readonly bool _verticalFont;

    /// <summary>The caller owns and disposes this font after an affirmative close.</summary>
    public DrawingFont? SelectedFont { get; private set; }

    public WinFormsFontEditor(DrawingFont initial)
    {
        _unit = initial.Unit;
        _characterSet = initial.GdiCharSet;
        _verticalFont = initial.GdiVerticalFont;
        Title = Localizer.Get(StringKeys.WinFormsFontEditorTitle);
        Width = 420;
        Height = 500;
        MinWidth = 330;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _family.Text = initial.OriginalFontName ?? initial.Name;
        _size.Text = initial.Size.ToString("R", CultureInfo.InvariantCulture);
        _bold.IsChecked = initial.Bold;
        _italic.IsChecked = initial.Italic;
        _underline.IsChecked = initial.Underline;
        _strikeout.IsChecked = initial.Strikeout;
        _bold.Content = Localizer.Get(StringKeys.WinFormsFontEditorBold);
        _italic.Content = Localizer.Get(StringKeys.WinFormsFontEditorItalic);
        _underline.Content = Localizer.Get(StringKeys.WinFormsFontEditorUnderline);
        _strikeout.Content = Localizer.Get(StringKeys.WinFormsFontEditorStrikeout);
        _families.ItemsSource = AvailableFamilies();
        _families.SelectionChanged += (_, _) =>
        {
            if (_families.SelectedItem is not string family)
            {
                return;
            }

            _family.Text = family;
        };
        _ok = MakeButton("FontOk", "WinFormsEditor_Ok", Save);
        _ok.IsDefault = true;
        var cancel = MakeButton("FontCancel", "WinFormsEditor_Cancel", () => Close(false));
        cancel.IsCancel = true;
        foreach (var field in new[] { _family, _size })
        {
            field.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty)
                {
                    RefreshPreview();
                }
            };
        }

        foreach (var flag in new[] { _bold, _italic, _underline, _strikeout })
        {
            flag.IsCheckedChanged += (_, _) => RefreshPreview();
        }

        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _bold, _italic, _underline, _strikeout },
        };
        foreach (var option in options.Children)
            option.Margin = new Thickness(0, 0, 8, 6);

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Localizer.Get(StringKeys.WinFormsFontEditorFamily) },
                _family,
                _families,
                new TextBlock { Text = Localizer.Get(StringKeys.WinFormsFontEditorSize) },
                _size,
                options,
                new TextBlock { Text = Localizer.Get(StringKeys.WinFormsFontEditorPreview) },
                new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    Padding = new Thickness(8),
                    MinHeight = 72,
                    ClipToBounds = true,
                    Child = _preview,
                },
                _error,
            },
        };
        Content = WinFormsEditorDialogLayout.Create(body, _ok, cancel);
        Opened += (_, _) =>
        {
            _family.Focus();
            _family.SelectAll();
        };
        RefreshPreview();
    }

    private static string[] AvailableFamilies()
    {
        // Font discovery may be unavailable in a headless environment. An
        // editable family field still accepts the document's original name.
        try
        {
            using var installed = new InstalledFontCollection();
            return installed.Families.Select(family => family.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
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

    private DrawingFontStyle Style =>
        (_bold.IsChecked == true ? DrawingFontStyle.Bold : 0) |
        (_italic.IsChecked == true ? DrawingFontStyle.Italic : 0) |
        (_underline.IsChecked == true ? DrawingFontStyle.Underline : 0) |
        (_strikeout.IsChecked == true ? DrawingFontStyle.Strikeout : 0);

    private bool TryCreateFont([NotNullWhen(true)] out DrawingFont? font)
    {
        font = null;
        if (string.IsNullOrWhiteSpace(_family.Text) ||
            !float.TryParse(_size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
            !float.IsFinite(size) || size <= 0)
        {
            return false;
        }

        try
        {
            font = new DrawingFont(_family.Text.Trim(), size, Style,
                _unit, _characterSet, _verticalFont);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void RefreshPreview()
    {
        if (!TryCreateFont(out var font))
        {
            _ok.IsEnabled = false;
            _error.Text = Localizer.Get(StringKeys.WinFormsEditorInvalidFont);
            return;
        }
        using (font)
        {
            _preview.Text = Localizer.Get(StringKeys.WinFormsFontEditorSample);
            _preview.FontFamily = new Avalonia.Media.FontFamily(font.Name);
            // Drawing.Font sizes are points; Avalonia uses device-independent pixels.
            _preview.FontSize = Math.Clamp(font.SizeInPoints * 96d / 72d, 1, 96);
            _preview.FontWeight = font.Bold ? FontWeight.Bold : FontWeight.Normal;
            _preview.FontStyle = font.Italic
                ? Avalonia.Media.FontStyle.Italic
                : Avalonia.Media.FontStyle.Normal;
            var decorations = new TextDecorationCollection();
            if (font.Underline)
                decorations.AddRange(TextDecorations.Underline);
            if (font.Strikeout)
                decorations.AddRange(TextDecorations.Strikethrough);
            _preview.TextDecorations = decorations.Count == 0 ? null : decorations;
        }
        _ok.IsEnabled = true;
        _error.Text = "";
    }

    private void Save()
    {
        if (!TryCreateFont(out var font))
        {
            RefreshPreview();
            return;
        }
        SelectedFont = font;
        Close(true);
    }
}
