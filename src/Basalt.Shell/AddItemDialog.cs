using Avalonia;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Shell;

/// <summary>The template picked in Add New Item, and the file name typed for it.</summary>
public sealed record AddItemChoice(ItemTemplate Template, string FileName);

/// <summary>
/// Solution Explorer's Add New Item: the templates a project offers, and the
/// name for the one picked, as Visual Studio's dialog does.
///
/// It asks for a name and checks that it can be one. Whether a file of that
/// name already sits in the folder is for the explorer to say, the same way it
/// does for New File, so the rule lives in one place.
/// </summary>
public sealed class AddItemDialog : Window
{
    private readonly ListBox _templates = new() { Height = 170 };
    private readonly TextBox _name = new();
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed };

    public AddItemDialog(IReadOnlyList<ItemTemplate> templates)
    {
        Title = Localizer.Get(StringKeys.AddItemTitle);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        foreach (var template in templates)
        {
            _templates.Items.Add(new ListBoxItem
            {
                Content = LabelFor(template),
                Tag = template
            });
        }

        // Choosing a template suggests its file name, as Visual Studio does
        // with Class1.vb, so the name is never left describing another template.
        _templates.SelectionChanged += (_, _) =>
        {
            if (ChosenTemplate is { } template) _name.Text = ItemTemplates.DefaultName(template);
        };

        if (templates.Count > 0) _templates.SelectedIndex = 0;

        var create = new Button
        {
            Content = Localizer.Get(StringKeys.AddItemCreate),
            Classes = { "primary" },
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = Localizer.Get(StringKeys.ButtonCancel),
            IsCancel = true
        };

        create.Click += (_, _) => Create();
        cancel.Click += (_, _) => Close(null);

        // Windows order: the primary action (Add) leads, Cancel follows.
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Localizer.Get(StringKeys.AddItemTemplates) },
                _templates,
                new TextBlock { Text = Localizer.Get(StringKeys.AddItemName) },
                _name,
                _error,
                DialogButtons.Row(create, cancel)
            }
        };

        Opened += (_, _) => _name.Focus();
    }

    private ItemTemplate? ChosenTemplate =>
        _templates.SelectedItem is ListBoxItem { Tag: ItemTemplate template } ? template : null;

    private void Create()
    {
        if (ChosenTemplate is not { } template) return;

        var fileName = ItemTemplates.FileNameFor(
            template, _name.Text ?? "", ExplorerFileOperations.IsVisualBasicReservedWord);
        if (fileName is null)
        {
            _error.Text = Localizer.Get(StringKeys.AddItemNameInvalid);
            return;
        }

        Close(new AddItemChoice(template, fileName));
    }

    private static string LabelFor(ItemTemplate template) => template switch
    {
        ItemTemplate.Class => Localizer.Get(StringKeys.AddItemClass),
        ItemTemplate.Module => Localizer.Get(StringKeys.AddItemModule),
        ItemTemplate.Interface => Localizer.Get(StringKeys.AddItemInterface),
        ItemTemplate.WindowsForm => Localizer.Get(StringKeys.AddItemWindowsForm),
        ItemTemplate.UserControl => Localizer.Get(StringKeys.AddItemUserControl),
        _ => Localizer.Get(StringKeys.AddItemEmptyFile)
    };
}
