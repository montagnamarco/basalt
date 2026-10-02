using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>PictureBox.LoadAsync/CancelAsync: the file read happens off the UI thread and the result is posted back through it.</summary>
public class PictureBoxAsyncTests
{
    private static string WriteTempImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        using var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        bitmap.Save(path, ImageFormat.Png);

        return path;
    }

    /// <summary>Pumps the dispatcher until the condition is true or five seconds pass: the one synchronisation every test below relies on to observe LoadCompleted.</summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition never became true.");

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    [AvaloniaFact]
    public void LoadAsync_loads_the_image_without_blocking_and_raises_LoadCompleted()
    {
        var path = WriteTempImage();
        try
        {
            var pictureBox = new PictureBox();
            var completed = false;
            AsyncCompletedEventArgs? result = null;
            pictureBox.LoadCompleted += (_, e) => { completed = true; result = e; };

            pictureBox.LoadAsync(path);

            // The image must not be loaded synchronously: the whole point of LoadAsync is not blocking the caller.
            Assert.False(completed);

            WaitUntil(() => completed);

            Assert.NotNull(result);
            Assert.Null(result!.Error);
            Assert.False(result.Cancelled);
            Assert.NotNull(pictureBox.Image);
            Assert.Equal(4, pictureBox.Image!.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void CancelAsync_on_a_load_still_running_completes_it_with_Cancelled_true_exactly_once()
    {
        using var gate = new ManualResetEventSlim();
        var pictureBox = new PictureBox { FetchOverride = BlockedRead(gate) };
        var completions = 0;
        AsyncCompletedEventArgs? result = null;
        pictureBox.LoadCompleted += (_, e) => { completions++; result = e; };

        pictureBox.LoadAsync("picture.png");
        pictureBox.CancelAsync();
        gate.Set();

        WaitUntil(() => completions > 0);
        pictureBox.PendingDelivery!.Wait(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, completions);
        Assert.True(result!.Cancelled);
        Assert.Null(result.Error);
        Assert.Null(pictureBox.Image);
    }

    [AvaloniaFact]
    public void CancelAsync_called_after_the_result_is_already_computed_still_delivers_it_normally()
    {
        var pictureBox = new PictureBox { FetchOverride = (_, _) => new Bitmap(4, 4, PixelFormat.Format32bppArgb) };
        var completions = 0;
        AsyncCompletedEventArgs? result = null;
        pictureBox.LoadCompleted += (_, e) => { completions++; result = e; };

        pictureBox.LoadAsync("picture.png");

        // The result is posted but the UI thread has not processed it yet: too late to cancel.
        Assert.True(pictureBox.PendingDelivery!.Wait(TimeSpan.FromSeconds(5)));
        pictureBox.CancelAsync();

        WaitUntil(() => completions > 0);

        Assert.Equal(1, completions);
        Assert.False(result!.Cancelled);
        Assert.Null(result.Error);
        Assert.NotNull(pictureBox.Image);
        Assert.Equal("picture.png", pictureBox.ImageLocation);
    }

    [AvaloniaFact]
    public void A_PictureBox_disposed_during_a_load_raises_nothing_afterwards()
    {
        using var gate = new ManualResetEventSlim();
        var pictureBox = new PictureBox { FetchOverride = BlockedRead(gate) };
        var completions = 0;
        pictureBox.LoadCompleted += (_, _) => completions++;

        pictureBox.LoadAsync("picture.png");
        pictureBox.Dispose();
        gate.Set();

        Assert.True(pictureBox.PendingDelivery!.Wait(TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, completions);
    }

    /// <summary>A read that waits for the test to open the gate, then honours the cancellation as FetchImage does.</summary>
    private static Func<string, CancellationToken, Image> BlockedRead(ManualResetEventSlim gate) => (_, token) =>
    {
        gate.Wait(TimeSpan.FromSeconds(5));
        token.ThrowIfCancellationRequested();

        return new Bitmap(4, 4, PixelFormat.Format32bppArgb);
    };

    [AvaloniaFact]
    public void A_missing_file_sets_ErrorImage_and_reports_the_error()
    {
        var pictureBox = new PictureBox { ErrorImage = new Bitmap(2, 2, PixelFormat.Format32bppArgb) };
        Exception? error = null;
        var completed = false;
        pictureBox.LoadCompleted += (_, e) => { error = e.Error; completed = true; };

        pictureBox.LoadAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-missing.png"));

        WaitUntil(() => completed);

        Assert.NotNull(error);
        Assert.Same(pictureBox.ErrorImage, pictureBox.Image);
    }
}
