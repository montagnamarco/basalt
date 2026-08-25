using System.Text;
using Basalt.Core.Text;

namespace Basalt.Tests;

/// <summary>
/// Working out what a file is encoded as.
///
/// The status bar used to say "UTF-8" whatever was on disk, which is a claim
/// rather than a reading. These check it now reads.
/// </summary>
public sealed class FileEncodingTests
{
    [Fact]
    public void ReadsAByteOrderMarkWhereThereIsOne()
    {
        Assert.Equal("UTF-8 with BOM", FileEncoding.Detect([0xEF, 0xBB, 0xBF, (byte)'a']));
        Assert.Equal("UTF-16 LE", FileEncoding.Detect([0xFF, 0xFE, (byte)'a', 0]));
        Assert.Equal("UTF-16 BE", FileEncoding.Detect([0xFE, 0xFF, 0, (byte)'a']));
    }

    [Fact]
    public void TakesPlainTextForUtf8()
    {
        Assert.Equal("UTF-8", FileEncoding.Detect(Encoding.ASCII.GetBytes("Module A")));
    }

    [Fact]
    public void TakesValidUtf8ForUtf8EvenWithoutAMark()
    {
        // Accented text is where a wrong guess shows: "città" is two bytes
        // for the last vowel in UTF-8 and one in Windows-1252.
        var bytes = new UTF8Encoding(false).GetBytes("Dim città = \"perché\"");

        Assert.Equal("UTF-8", FileEncoding.Detect(bytes));
    }

    [Fact]
    public void FallsBackToWindows1252WhenTheBytesAreNotUtf8()
    {
        // The same text written as Latin-1 cannot be read as UTF-8, which is
        // exactly how an old file gives itself away.
        var bytes = Encoding.Latin1.GetBytes("Dim città = \"perché\"");

        Assert.Equal("Windows-1252", FileEncoding.Detect(bytes));
    }

    [Fact]
    public void ReadsAnEmptyFileAsUtf8()
    {
        Assert.Equal("UTF-8", FileEncoding.Detect([]));
    }

    [Fact]
    public void OffersTheEncodingsAFileCanBeReopenedAs()
    {
        Assert.Contains("UTF-8", FileEncoding.Names);
        Assert.Contains("Windows-1252", FileEncoding.Names);
        Assert.Contains("UTF-16 LE", FileEncoding.Names);
    }

    [Fact]
    public void GivesAnEncodingForEveryNameItOffers()
    {
        // A name in the list with nothing behind it would fail only when
        // someone picked it.
        Assert.All(FileEncoding.Names, name => Assert.NotNull(FileEncoding.For(name)));
    }

    [Fact]
    public void WritesAMarkOnlyWhereTheNameSaysSo()
    {
        Assert.Empty(FileEncoding.For("UTF-8").GetPreamble());
        Assert.NotEmpty(FileEncoding.For("UTF-8 with BOM").GetPreamble());
    }

    [Fact]
    public async Task ReadsAFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(path, "Dim città", Encoding.Latin1);

            Assert.Equal("Windows-1252", await FileEncoding.DetectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaysUtf8ForAFileItCannotRead()
    {
        // Rather than throwing while the status bar is being drawn.
        Assert.Equal("UTF-8",
            await FileEncoding.DetectAsync("/no/such/file/anywhere.txt"));
    }

    [Fact]
    public void RoundTripsTextThroughTheEncodingItNames()
    {
        const string text = "Dim città = \"perché\"";

        foreach (var name in new[] { "UTF-8", "UTF-8 with BOM", "UTF-16 LE", "Windows-1252" })
        {
            var encoding = FileEncoding.For(name);
            var written = encoding.GetString(encoding.GetBytes(text));

            Assert.Equal(text, written);
        }
    }
}
