using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Settings;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Settings reaching the editor they describe.
///
/// A setting that is stored but never applied looks to the user exactly like
/// one that does not work, so these check the editor itself rather than the
/// file on disk.
/// </summary>
public sealed class SettingsApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-apply", Guid.NewGuid().ToString("N"));

    public SettingsApplyTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, CodeEditor Code, TextEditor Editor)> OpenAsync()
    {
        var file = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(file, "Module A\nEnd Module");

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();

        return (host, code, editor);
    }

    [AvaloniaFact]
    public async Task ChangesTheFontOfAnEditorAlreadyOpen()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        var settings = new IdeSettings();
        settings.Editor.FontSize = 21;

        code.ApplySettings(settings);

        Assert.Equal(21, editor.FontSize);
    }

    [AvaloniaFact]
    public async Task ChangesTheIndentation()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        var settings = new IdeSettings();
        settings.Editor.IndentationSize = 2;
        settings.Editor.ConvertTabsToSpaces = false;

        code.ApplySettings(settings);

        Assert.Equal(2, editor.Options.IndentationSize);
        Assert.False(editor.Options.ConvertTabsToSpaces);
    }

    [AvaloniaFact]
    public async Task TurnsLineNumbersAndWrappingOnAndOff()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        var settings = new IdeSettings();
        settings.Editor.ShowLineNumbers = false;
        settings.Editor.WordWrap = true;

        code.ApplySettings(settings);

        Assert.False(editor.ShowLineNumbers);
        Assert.True(editor.WordWrap);
    }

    [AvaloniaFact]
    public async Task TurnsBracketCompletionOff()
    {
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        var settings = new IdeSettings();
        settings.Editor.AutoCloseBrackets = false;

        code.ApplySettings(settings);

        Assert.False(code.AutoCloseBrackets);
    }

    [AvaloniaFact]
    public async Task LetsALanguageOverrideTheGeneralSetting()
    {
        // A user may want conventions applied while typing Visual Basic but
        // not in every file the IDE can open.
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        var settings = new IdeSettings();
        settings.Editor.FormatWhileTyping = true;
        settings.SetLanguageSetting("vb", "FormatWhileTyping", "false");

        code.ApplySettings(settings);

        Assert.False(code.AutoFormatWhileTyping);
    }

    [AvaloniaFact]
    public async Task FallsBackToTheGeneralSettingWhenTheLanguageSaysNothing()
    {
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        var settings = new IdeSettings();
        settings.Editor.FormatWhileTyping = false;

        code.ApplySettings(settings);

        Assert.False(code.AutoFormatWhileTyping);
    }

    [AvaloniaFact]
    public async Task ShowsWhitespaceWhenAskedTo()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        var settings = new IdeSettings();
        settings.Editor.ShowWhitespace = true;

        code.ApplySettings(settings);

        Assert.True(editor.Options.ShowSpaces);
        Assert.True(editor.Options.ShowTabs);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
