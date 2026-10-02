using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Web;

/// <summary>
/// WebBrowser has no HTML engine: it keeps the history, the
/// Navigating/Navigated/DocumentCompleted sequence and the asynchronous
/// delivery through the UI message loop a real control would also use.
/// </summary>
public class WebBrowserTests
{
    /// <summary>
    /// Drains the dispatcher until <paramref name="condition"/> is true, so
    /// a test never races the Dispatcher.UIThread.Post a navigation uses -
    /// and never hangs forever if a defect stops the event from arriving.
    /// </summary>
    private static void PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met within the timeout.");

            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void Navigate_delivers_Navigating_then_Navigated_then_DocumentCompleted_asynchronously()
    {
        using var browser = new WebBrowser();
        var order = new List<string>();
        browser.Navigating += (_, e) => order.Add($"Navigating:{e.Url}");
        browser.Navigated += (_, e) => order.Add($"Navigated:{e.Url}");
        browser.DocumentCompleted += (_, e) => order.Add($"DocumentCompleted:{e.Url}");

        var target = new Uri("https://example.test/");
        browser.Navigate(target);

        // Nothing has happened yet: the navigation is queued on the
        // dispatcher, not run inline with the call that triggered it.
        Assert.Empty(order);
        Assert.Null(browser.Url);
        Assert.True(browser.IsBusy);
        Assert.Equal(WebBrowserReadyState.Loading, browser.ReadyState);

        PumpUntil(() => order.Count == 3);

        Assert.Equal(
            [$"Navigating:{target}", $"Navigated:{target}", $"DocumentCompleted:{target}"],
            order);
        Assert.Equal(target, browser.Url);
        Assert.False(browser.IsBusy);
        Assert.Equal(WebBrowserReadyState.Complete, browser.ReadyState);
    }

    [AvaloniaFact]
    public void Cancelling_Navigating_leaves_the_Url_unchanged()
    {
        using var browser = new WebBrowser();
        var first = new Uri("https://one.test/");
        browser.Navigate(first);
        PumpUntil(() => browser.Url == first);

        var navigatedRaised = false;
        browser.Navigating += (_, e) => e.Cancel = true;
        browser.Navigated += (_, _) => navigatedRaised = true;

        browser.Navigate(new Uri("https://two.test/"));

        // The cancelled navigation still has to run its one posted step
        // before it is safe to assert nothing changed.
        PumpUntil(() => !browser.IsBusy);

        Assert.Equal(first, browser.Url);
        Assert.False(navigatedRaised);
        Assert.Equal(WebBrowserReadyState.Complete, browser.ReadyState);
    }

    [AvaloniaFact]
    public void GoBack_and_GoForward_follow_the_history_and_raise_CanGoBack_CanGoForward_changes()
    {
        using var browser = new WebBrowser();
        var canGoBackChanges = new List<bool>();
        var canGoForwardChanges = new List<bool>();
        browser.CanGoBackChanged += (_, _) => canGoBackChanges.Add(browser.CanGoBack);
        browser.CanGoForwardChanged += (_, _) => canGoForwardChanges.Add(browser.CanGoForward);

        var first = new Uri("https://one.test/");
        var second = new Uri("https://two.test/");
        var third = new Uri("https://three.test/");

        browser.Navigate(first);
        PumpUntil(() => browser.Url == first);
        browser.Navigate(second);
        PumpUntil(() => browser.Url == second);
        browser.Navigate(third);
        PumpUntil(() => browser.Url == third);

        Assert.True(browser.CanGoBack);
        Assert.False(browser.CanGoForward);
        Assert.Equal([true], canGoBackChanges);

        Assert.True(browser.GoBack());
        PumpUntil(() => browser.Url == second);

        Assert.Equal(second, browser.Url);
        Assert.True(browser.CanGoBack);
        Assert.True(browser.CanGoForward);
        Assert.Equal([true], canGoForwardChanges);

        Assert.True(browser.GoBack());
        PumpUntil(() => browser.Url == first);

        Assert.Equal(first, browser.Url);
        Assert.False(browser.CanGoBack);
        Assert.True(browser.CanGoForward);
        Assert.Equal([true, false], canGoBackChanges);

        Assert.False(browser.GoBack());

        Assert.True(browser.GoForward());
        PumpUntil(() => browser.Url == second);
        Assert.Equal(second, browser.Url);

        Assert.True(browser.GoForward());
        PumpUntil(() => browser.Url == third);
        Assert.Equal(third, browser.Url);
        Assert.False(browser.CanGoForward);

        Assert.False(browser.GoForward());
    }

    [AvaloniaFact]
    public void A_new_Navigate_after_GoBack_truncates_the_forward_history()
    {
        using var browser = new WebBrowser();
        var first = new Uri("https://one.test/");
        var second = new Uri("https://two.test/");
        var third = new Uri("https://three.test/");

        browser.Navigate(first);
        PumpUntil(() => browser.Url == first);
        browser.Navigate(second);
        PumpUntil(() => browser.Url == second);

        browser.GoBack();
        PumpUntil(() => browser.Url == first);
        Assert.True(browser.CanGoForward);

        browser.Navigate(third);
        PumpUntil(() => browser.Url == third);

        Assert.False(browser.CanGoForward);
        Assert.True(browser.CanGoBack);

        Assert.True(browser.GoBack());
        PumpUntil(() => browser.Url == first);
        Assert.Equal(first, browser.Url);
    }

    [AvaloniaFact]
    public void DocumentText_is_stored_without_touching_the_navigation_history()
    {
        using var browser = new WebBrowser();

        browser.DocumentText = "<html><body>hello</body></html>";

        Assert.Equal("<html><body>hello</body></html>", browser.DocumentText);
        Assert.Null(browser.Url);
        Assert.False(browser.CanGoBack);
        Assert.False(browser.CanGoForward);
    }

    [AvaloniaFact]
    public void Document_is_always_null()
    {
        using var browser = new WebBrowser();
        browser.Navigate(new Uri("https://one.test/"));
        PumpUntil(() => browser.Url is not null);

        Assert.Null(browser.Document);
    }

    [AvaloniaFact]
    public void Stop_right_after_Navigate_cancels_the_already_posted_completion()
    {
        using var browser = new WebBrowser();
        var navigatedRaised = false;
        var documentCompletedRaised = false;
        browser.Navigated += (_, _) => navigatedRaised = true;
        browser.DocumentCompleted += (_, _) => documentCompletedRaised = true;

        browser.Navigate(new Uri("https://one.test/"));
        browser.Stop();

        // Drain whatever CompleteNavigation was already posted: it must not
        // complete behind Stop()'s back.
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigatedRaised);
        Assert.False(documentCompletedRaised);
        Assert.Null(browser.Url);
        Assert.False(browser.IsBusy);
    }

    [AvaloniaFact]
    public void A_Navigate_from_inside_Navigating_redirects_and_the_first_page_never_completes()
    {
        using var browser = new WebBrowser();
        var first = new Uri("https://first.test/");
        var second = new Uri("https://second.test/");
        var completed = new List<Uri>();
        browser.Navigating += (_, e) => { if (e.Url == first) browser.Navigate(second); };
        browser.DocumentCompleted += (_, e) => completed.Add(e.Url!);

        browser.Navigate(first);
        PumpUntil(() => completed.Count > 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([second], completed);
        Assert.Equal(second, browser.Url);
        Assert.False(browser.CanGoBack);
        Assert.Equal(WebBrowserReadyState.Complete, browser.ReadyState);
    }

    [AvaloniaFact]
    public void Stop_from_inside_Navigating_stops_that_navigation()
    {
        using var browser = new WebBrowser();
        var navigated = 0;
        browser.Navigating += (_, _) => browser.Stop();
        browser.Navigated += (_, _) => navigated++;

        browser.Navigate(new Uri("https://one.test/"));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, navigated);
        Assert.Null(browser.Url);
        Assert.False(browser.IsBusy);
        Assert.NotEqual(WebBrowserReadyState.Complete, browser.ReadyState);
    }

    [AvaloniaFact]
    public void A_second_Navigate_before_the_dispatcher_runs_supersedes_the_first()
    {
        using var browser = new WebBrowser();
        var completed = new List<Uri?>();
        browser.DocumentCompleted += (_, e) => completed.Add(e.Url);

        var a = new Uri("https://one.test/");
        var b = new Uri("https://two.test/");

        browser.Navigate(a);
        browser.Navigate(b);

        PumpUntil(() => browser.Url == b);

        Assert.Equal([b], completed);
        Assert.Equal(b, browser.Url);
        Assert.False(browser.CanGoBack);
        Assert.False(browser.CanGoForward);
    }
}
