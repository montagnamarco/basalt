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
        var path = WriteTempImage();
        try
        {
            var pictureBox = new PictureBox();
            var completions = 0;
            AsyncCompletedEventArgs? result = null;
            pictureBox.LoadCompleted += (_, e) => { completions++; result = e; };

            pictureBox.LoadAsync(path);
            // Cancelled on the very next line, before the background task has
            // any realistic chance to have even started: Task.Run(Func<T>, token)
            // skips invoking the delegate at all once the token is already
            // cancelled by the time the thread pool would start it.
            pictureBox.CancelAsync();

            WaitUntil(() => completions > 0);

            Assert.Equal(1, completions);
            Assert.NotNull(result);
            Assert.True(result!.Cancelled);
            Assert.Null(result.Error);
            Assert.Null(pictureBox.Image);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void CancelAsync_called_after_the_result_is_already_computed_still_delivers_it_normally()
    {
        var path = WriteTempImage();
        try
        {
            var pictureBox = new PictureBox();
            var completions = 0;
            AsyncCompletedEventArgs? result = null;
            pictureBox.LoadCompleted += (_, e) => { completions++; result = e; };

            pictureBox.LoadAsync(path);

            // Give the background read (a handful of bytes from a local temp
            // file) time to finish and its continuation to reach
            // Dispatcher.UIThread.Post, all without pumping the dispatcher
            // yet: the point is to call CancelAsync in the window between
            // "the result is ready and posted" and "the UI thread has
            // actually processed that posted job" - too late to cancel
            // anything, the same way BackgroundWorker.CancelAsync treats
            // work that already finished.
            Thread.Sleep(300);

            pictureBox.CancelAsync();

            WaitUntil(() => completions > 0);

            Assert.Equal(1, completions);
            Assert.NotNull(result);
            Assert.False(result!.Cancelled);
            Assert.Null(result.Error);
            Assert.NotNull(pictureBox.Image);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
