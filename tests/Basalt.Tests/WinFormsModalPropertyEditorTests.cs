using System.Drawing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public sealed class WinFormsModalPropertyEditorTests
{
    [AvaloniaFact]
    public async Task ColorEditorKeepsSystemIdentityAndCommitsTransparentCustomHexOnlyOnOk()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var system = new WinFormsColorEditor(SystemColors.Window);
            var systemResult = system.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Find<ComboBox>(system, "ColorKind").SelectedIndex);
            Assert.Null(system.SelectedColor);
            Click(system, "ColorOk");
            Assert.True(await systemResult);
            Assert.Equal(SystemColors.Window, system.SelectedColor);
            Assert.True(system.SelectedColor!.Value.IsSystemColor);

            var custom = new WinFormsColorEditor(Color.Red);
            var customResult = custom.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(custom, "ColorKind").SelectedIndex = 2;
            Find<TextBox>(custom, "ColorHex").Text = "#7F123456";
            Click(custom, "ColorOk");
            Assert.True(await customResult);
            Assert.Equal(Color.FromArgb(0x7F, 0x12, 0x34, 0x56), custom.SelectedColor);
            Assert.Equal("System.Drawing.Color.FromArgb(127, 18, 52, 86)",
                WinFormsDesignerValues.Serialize(custom.SelectedColor));
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task InvalidHexCannotCommitAndCancelNeverExposesADraft()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var editor = new WinFormsColorEditor(Color.Blue);
            var result = editor.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(editor, "ColorKind").SelectedIndex = 2;
            Find<TextBox>(editor, "ColorHex").Text = "#GG1234";
            Assert.False(Find<Button>(editor, "ColorOk").IsEnabled);
            Assert.Null(editor.SelectedColor);
            Click(editor, "ColorCancel");
            Assert.False(await result);
            Assert.Null(editor.SelectedColor);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task FontEditorPreservesUnitCharsetAndVerticalFlagAndOwnsOnlyCommittedFont()
    {
        using var original = new Font("Tahoma", 12f, FontStyle.Italic,
            GraphicsUnit.Pixel, 42, true);
        var owner = new Window();
        owner.Show();
        try
        {
            var editor = new WinFormsFontEditor(original);
            var result = editor.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(editor.SelectedFont);
            Find<TextBox>(editor, "FontSize").Text = "14.5";
            Find<CheckBox>(editor, "FontBold").IsChecked = true;
            Find<CheckBox>(editor, "FontUnderline").IsChecked = true;
            Click(editor, "FontOk");
            Assert.True(await result);
            using var selected = Assert.IsType<Font>(editor.SelectedFont);
            Assert.Equal(14.5f, selected.Size);
            Assert.Equal(FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, selected.Style);
            Assert.Equal(GraphicsUnit.Pixel, selected.Unit);
            Assert.Equal((byte)42, selected.GdiCharSet);
            Assert.True(selected.GdiVerticalFont);
            Assert.Equal(12f, original.Size);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task FontCancelDoesNotAllocateASelectedFontAndInvalidSizeCannotCommit()
    {
        using var original = new Font("Tahoma", 10f);
        var owner = new Window();
        owner.Show();
        try
        {
            var editor = new WinFormsFontEditor(original);
            var result = editor.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Find<TextBox>(editor, "FontSize").Text = "NaN";
            Assert.False(Find<Button>(editor, "FontOk").IsEnabled);
            Click(editor, "FontCancel");
            Assert.False(await result);
            Assert.Null(editor.SelectedFont);
        }
        finally
        {
            owner.Close();
        }
    }

    private static T Find<T>(Window editor, string name) where T : Control =>
        editor.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Window editor, string name) =>
        Find<Button>(editor, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
