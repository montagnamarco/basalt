using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Printing;

[Collection(SharedStateCollection.Name)]
public class PrintControllerWithStatusDialogTests
{
    [AvaloniaFact]
    public void Worker_completion_before_dispatch_does_not_leave_a_status_window_open()
    {
        using var document = new PrintDocument();
        using var image = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(image);
        var wrapper = new PrintControllerWithStatusDialog(new RecordingController(graphics), "Finished job");

        Task.Run(() =>
        {
            wrapper.OnStartPrint(document, new PrintEventArgs());
            wrapper.OnEndPrint(document, new PrintEventArgs());
        }).GetAwaiter().GetResult();

        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Application.OpenForms.Cast<Form>(), form => form.Text == "Finished job");
    }

    [AvaloniaFact]
    public void Worker_print_shows_and_closes_progress_without_a_forms_message_loop()
    {
        using var document = new PrintDocument();
        using var image = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(image);
        var wrapper = new PrintControllerWithStatusDialog(new RecordingController(graphics), "Active job");

        Task.Run(() => wrapper.OnStartPrint(document, new PrintEventArgs())).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        var status = Assert.Single(Application.OpenForms.Cast<Form>(), form => form.Text == "Active job");
        Assert.Contains(status.Controls.Cast<Control>(), control => control is Label { Text: "Printing page 1" });

        Task.Run(() => wrapper.OnEndPrint(document, new PrintEventArgs())).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Application.OpenForms.Cast<Form>(), form => form.Text == "Active job");
    }

    [Fact]
    public void Wrapper_forwards_each_print_callback_and_cancellation_to_underlying_controller()
    {
        using var image = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(image);
        var underlying = new RecordingController(graphics);
        var wrapper = new PrintControllerWithStatusDialog(underlying, "Job");
        using var document = new PrintDocument();
        var print = new PrintEventArgs();
        var page = new PrintPageEventArgs(graphics, Rectangle.Empty, Rectangle.Empty, document.DefaultPageSettings);

        Assert.True(wrapper.IsPreview);
        wrapper.OnStartPrint(document, print);
        wrapper.Cancel();
        Assert.Same(graphics, wrapper.OnStartPage(document, page));
        wrapper.OnEndPage(document, page);
        wrapper.OnEndPrint(document, print);

        Assert.Equal(["start", "page-start", "page-end", "end"], underlying.Calls);
        Assert.True(page.Cancel);
        Assert.True(print.Cancel);
    }

    private sealed class RecordingController(Graphics graphics) : PrintController
    {
        internal List<string> Calls { get; } = [];

        public override bool IsPreview => true;

        public override void OnStartPrint(PrintDocument document, PrintEventArgs e) => Calls.Add("start");

        public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
        {
            Calls.Add("page-start");
            return graphics;
        }

        public override void OnEndPage(PrintDocument document, PrintPageEventArgs e) => Calls.Add("page-end");

        public override void OnEndPrint(PrintDocument document, PrintEventArgs e) => Calls.Add("end");
    }
}
