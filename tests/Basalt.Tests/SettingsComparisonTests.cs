using Basalt.Core.Settings;

namespace Basalt.Tests;

/// <summary>
/// Telling changed settings from default ones.
///
/// What the settings window marks with a dot, so a wrong answer here shows up
/// as every page looking changed on a fresh install.
/// </summary>
public sealed class SettingsComparisonTests
{
    [Fact]
    public void FindsNothingChangedInFreshSettings()
    {
        // A collection property holds a different instance in each settings
        // object, so comparing by reference would report every group changed.
        Assert.Empty(SettingsComparison.ChangedGroups(new IdeSettings()));
    }

    [Fact]
    public void NoticesAChangedSetting()
    {
        var settings = new IdeSettings();
        settings.Editor.FontSize = 22;

        Assert.Contains("FontSize", SettingsComparison.ChangedIn(settings, "Editor"));
        Assert.True(SettingsComparison.HasChangesIn(settings, "Editor"));
    }

    [Fact]
    public void KeepsOneGroupsChangeOutOfAnother()
    {
        var settings = new IdeSettings();
        settings.Editor.FontSize = 22;

        Assert.False(SettingsComparison.HasChangesIn(settings, "Appearance"));
        Assert.Equal(["Editor"], SettingsComparison.ChangedGroups(settings));
    }

    [Fact]
    public void NoticesAChangedCollection()
    {
        var settings = new IdeSettings();
        settings.Keyboard.Shortcuts["build.solution"] = "Ctrl+Alt+B";

        Assert.True(SettingsComparison.HasChangesIn(settings, "Keyboard"));
    }

    [Fact]
    public void PutsAGroupBackToItsDefaults()
    {
        var settings = new IdeSettings();
        settings.Editor.FontSize = 22;
        settings.Editor.WordWrap = true;

        SettingsComparison.ResetGroup(settings, "Editor");

        Assert.False(SettingsComparison.HasChangesIn(settings, "Editor"));
        Assert.Equal(new IdeSettings().Editor.FontSize, settings.Editor.FontSize);
    }

    [Fact]
    public void LeavesTheOtherGroupsAloneWhenResettingOne()
    {
        var settings = new IdeSettings();
        settings.Editor.FontSize = 22;
        settings.Appearance.Theme = AppTheme.SolarizedDark;

        SettingsComparison.ResetGroup(settings, "Editor");

        Assert.Equal(AppTheme.SolarizedDark, settings.Appearance.Theme);
    }

    [Fact]
    public void SaysNothingAboutAGroupThatDoesNotExist()
    {
        Assert.Empty(SettingsComparison.ChangedIn(new IdeSettings(), "NoSuchGroup"));
    }
}
