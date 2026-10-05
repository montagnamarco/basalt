using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsEditorDialogUsabilityTests
{
    [AvaloniaFact]
    public async Task InvalidColumnDraftSurvivesRowSelectionUntilRepaired()
    {
        var owner = new Window();
        owner.Show();
        var editor = new WinFormsCollectionEditor(new[]
        {
            new WinFormsColumnDefinition("First", "DataGridViewTextBoxColumn", "First"),
            new WinFormsColumnDefinition("Second", "DataGridViewTextBoxColumn", "Second"),
        });
        var result = editor.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Find<TextBox>(editor, "CollectionWidth").Text = "invalid";
            Assert.False(Find<Button>(editor, "CollectionSave").IsEnabled);
            var entries = Find<ListBox>(editor, "CollectionEntries");
            entries.SelectedIndex = 1;
            Assert.False(Find<Button>(editor, "CollectionSave").IsEnabled);
            entries.SelectedIndex = 0;
            Assert.Equal("invalid", Find<TextBox>(editor, "CollectionWidth").Text);
            Find<TextBox>(editor, "CollectionWidth").Text = "120";
            Assert.True(Find<Button>(editor, "CollectionSave").IsEnabled);
            Click(editor, "CollectionSave");
            Assert.True(await result);
            Assert.Equal(120, editor.Columns[0].Width);
        }
        finally
        {
            editor.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task MultilineItemCannotCloseDialogThroughSave()
    {
        var owner = new Window();
        owner.Show();
        var editor = new WinFormsCollectionEditor(new[] { "First" });
        var result = editor.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Find<TextBox>(editor, "CollectionText").Text = "First\nSecond";
            Assert.False(Find<Button>(editor, "CollectionSave").IsEnabled);
            Click(editor, "CollectionSave");
            Assert.False(result.IsCompleted);
            Find<TextBox>(editor, "CollectionText").Text = "Repaired";
            Click(editor, "CollectionSave");
            Assert.True(await result);
            Assert.Equal(new[] { "Repaired" }, editor.Items);
        }
        finally
        {
            editor.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void FontPreviewUsesDeviceIndependentSizeAndBothDecorations()
    {
        using var original = new Font("Tahoma", 12, FontStyle.Underline | FontStyle.Strikeout);
        var editor = new WinFormsFontEditor(original);
        editor.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var preview = Find<TextBlock>(editor, "FontPreview");
            Assert.Equal(16d, preview.FontSize);
            Assert.Equal(2, preview.TextDecorations!.Count);
            editor.Width = editor.MinWidth;
            editor.Height = editor.MinHeight;
            editor.UpdateLayout();
            var ok = Find<Button>(editor, "FontOk");
            var location = ok.TranslatePoint(default, editor)!.Value;
            Assert.True(location.Y >= 0);
            Assert.True(location.Y + ok.Bounds.Height <= editor.ClientSize.Height);
            Assert.True(ok.Bounds.Width > 0);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public async Task ColorCategoryChangesKeepNamedSelectionAndCustomDraft()
    {
        var owner = new Window();
        owner.Show();
        var editor = new WinFormsColorEditor(Color.Red);
        var result = editor.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var kind = Find<ComboBox>(editor, "ColorKind");
            var names = Find<ListBox>(editor, "ColorNames");
            names.SelectedItem = "Green";
            kind.SelectedIndex = 1;
            kind.SelectedIndex = 0;
            Assert.Equal("Green", names.SelectedItem);
            kind.SelectedIndex = 2;
            var hex = Find<TextBox>(editor, "ColorHex");
            Assert.Equal(WinFormsColorEditor.FormatHex(Color.Green), hex.Text);
            hex.Text = "#invalid";
            kind.SelectedIndex = 0;
            kind.SelectedIndex = 2;
            Assert.Equal("#invalid", hex.Text);
            Assert.False(Find<Button>(editor, "ColorOk").IsEnabled);
            Click(editor, "ColorCancel");
            Assert.False(await result);
            Assert.Null(editor.SelectedColor);
        }
        finally
        {
            editor.Close();
            owner.Close();
        }
    }

    [Theory]
    [InlineData("##123456")]
    [InlineData("###FF123456")]
    public void HexColorRejectsRepeatedPrefix(string text)
    {
        Assert.False(WinFormsColorEditor.TryParseHex(text, out _));
    }

    private static T Find<T>(Window editor, string name) where T : Control =>
        editor.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Window editor, string name) =>
        Find<Button>(editor, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
