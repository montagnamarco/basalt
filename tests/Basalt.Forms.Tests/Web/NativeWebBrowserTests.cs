using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Web;

public class NativeWebBrowserTests
{
    [AvaloniaFact]
    public void Hosted_navigation_waits_for_native_engine_completion()
    {
        using var browser = new WebBrowser();
        var view = browser.NativeView;
        var completed = 0;
        browser.DocumentCompleted += (_, _) => completed++;

        var target = new Uri("https://example.test/");
        browser.Navigate(target);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(target, Assert.IsType<NativeWebView>(view).Source);
        Assert.Equal(0, completed);
        Assert.True(browser.IsBusy);
        Assert.Equal(WebBrowserReadyState.Loading, browser.ReadyState);
    }

    [AvaloniaFact]
    public void Document_text_is_sent_to_native_view_without_fabricating_completion()
    {
        using var browser = new WebBrowser();
        _ = browser.NativeView;
        var completed = 0;
        browser.DocumentCompleted += (_, _) => completed++;

        browser.DocumentText = "<html><body>hello</body></html>";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("<html><body>hello</body></html>", browser.DocumentText);
        Assert.Equal(0, completed);
    }

    [AvaloniaFact]
    public void Internet_Explorer_only_features_fail_explicitly()
    {
        using var browser = new WebBrowser();

        Assert.Throws<PlatformNotSupportedException>(() => browser.ObjectForScripting = new object());
        Assert.Throws<PlatformNotSupportedException>(() => browser.Navigate(new Uri("https://example.test/"), "frame"));
        Assert.Throws<PlatformNotSupportedException>(() => browser.ShowSaveAsDialog());
    }
}
