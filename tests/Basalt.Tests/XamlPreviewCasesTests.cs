using Avalonia.Headless.XUnit;
using Basalt.Designer;

namespace Basalt.Tests;

/// <summary>
/// The XAML a real window actually contains.
/// </summary>
/// <remarks>
/// The sample used to check the designer was written to be easy: a Canvas and
/// two controls, no x:Class, no styles, no design-time attributes. A file
/// someone actually has carries all of those, and the preview showed white.
/// </remarks>
public sealed class XamlPreviewCasesTests
{
    private static string Failure(string xaml)
    {
        var result = new XamlPreviewRenderer().Render(xaml);

        return result.Succeeded ? "" : result.Error ?? "unknown";
    }

    [AvaloniaFact]
    public void AWindowWithCodeBehindRenders()
    {
        // x:Class names a type the loader wants to find, and at design time
        // it is almost never built yet.
        Assert.Equal("", Failure("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="MyApp.MainWindow" Title="Hello">
              <StackPanel><Button Content="Go" /></StackPanel>
            </Window>
            """));
    }

    [AvaloniaFact]
    public void AnEmptyWindowRenders()
    {
        Assert.Equal("", Failure("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """));
    }

    [AvaloniaFact]
    public void DesignTimeAttributesAreTolerated()
    {
        // Every window the Avalonia templates generate carries these.
        Assert.Equal("", Failure("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                    mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450">
              <TextBlock Text="hello" />
            </Window>
            """));
    }

    [AvaloniaFact]
    public void LocalStylesRender()
    {
        Assert.Equal("", Failure("""
            <Window xmlns="https://github.com/avaloniaui">
              <Window.Styles>
                <Style Selector="Button"><Setter Property="Width" Value="80" /></Style>
              </Window.Styles>
              <Button Content="Go" />
            </Window>
            """));
    }

    [AvaloniaFact]
    public void AUserControlRenders()
    {
        Assert.Equal("", Failure("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="MyApp.Panel">
              <Grid><Button Content="Go" /></Grid>
            </UserControl>
            """));
    }
}
