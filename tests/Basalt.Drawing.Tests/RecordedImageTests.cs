using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace Basalt.Drawing.Tests;

public class RecordedImageTests
{
    [Fact]
    public void DrawCachedBitmap_survives_picture_recording_and_replay()
    {
        using var source = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Lime);

        using var recorder = new SKPictureRecorder();
        var recordingCanvas = recorder.BeginRecording(new SKRect(0, 0, 20, 20));
        using (var graphics = Graphics.FromCanvas(recordingCanvas))
        {
            using var cached = new CachedBitmap(source, graphics);
            graphics.DrawCachedBitmap(cached, 3, 3);
        }

        using var picture = recorder.EndRecording();
        using var surface = new SKBitmap(20, 20);
        using (var canvas = new SKCanvas(surface)) canvas.DrawPicture(picture);

        var pixel = surface.GetPixel(6, 6);
        Assert.True(pixel.Green > 180 && pixel.Red < 100 && pixel.Blue < 100, $"Recorded pixel: {pixel}");
    }

    [Fact]
    public void DrawImage_survives_picture_recording_and_replay()
    {
        var source = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Lime);

        using var recorder = new SKPictureRecorder();
        var recordingCanvas = recorder.BeginRecording(new SKRect(0, 0, 20, 20));
        using (var graphics = Graphics.FromCanvas(recordingCanvas)) graphics.DrawImage(source, 3, 3, 8, 8);
        source.Dispose();

        using var picture = recorder.EndRecording();
        using var surface = new SKBitmap(20, 20);
        using (var canvas = new SKCanvas(surface)) canvas.DrawPicture(picture);

        var pixel = surface.GetPixel(6, 6);
        Assert.True(pixel.Green > 180 && pixel.Red < 100 && pixel.Blue < 100, $"Recorded pixel: {pixel}");
    }
}
