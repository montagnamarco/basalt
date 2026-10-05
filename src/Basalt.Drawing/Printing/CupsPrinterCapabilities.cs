using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace System.Drawing.Printing;

internal static class CupsPrinterCapabilities
{
    private static readonly Regex SizePattern = new(@"(?<width>\d+(?:\.\d+)?)x(?<height>\d+(?:\.\d+)?)(?<unit>mm|in)$", RegexOptions.CultureInvariant);
    private static readonly Regex ResolutionPattern = new(@"^(?<x>\d+)(?:x(?<y>\d+))?dpi$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static PrinterCapabilities Discover(string printerName)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("lpoptions")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-p");
            process.StartInfo.ArgumentList.Add(printerName);
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.Environment["LC_ALL"] = "C";
            process.Start();

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return PrinterCapabilities.Unknown;
            }

            _ = errors.Result;
            return process.ExitCode == 0 ? Parse(output.Result) : PrinterCapabilities.Unknown;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return PrinterCapabilities.Unknown;
        }
    }

    internal static PrinterCapabilities Parse(string output)
    {
        bool? duplex = null;
        bool? color = null;
        var papers = new List<PaperSize>();
        var resolutions = new List<PrinterResolution>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Split('/')[0].Trim();
            var choices = line[(colon + 1)..].Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static choice => choice.TrimStart('*')).ToArray();

            if (key.Equals("Duplex", StringComparison.OrdinalIgnoreCase) || key.Equals("sides", StringComparison.OrdinalIgnoreCase))
                duplex = choices.Any(static choice => choice.Contains("two-sided", StringComparison.OrdinalIgnoreCase)
                    || choice.Contains("DuplexNoTumble", StringComparison.OrdinalIgnoreCase)
                    || choice.Contains("DuplexTumble", StringComparison.OrdinalIgnoreCase));

            if (key.Equals("ColorModel", StringComparison.OrdinalIgnoreCase)
                || key.Equals("ColorMode", StringComparison.OrdinalIgnoreCase)
                || key.Equals("print-color-mode", StringComparison.OrdinalIgnoreCase))
                color = choices.Any(static choice => choice.Equals("Color", StringComparison.OrdinalIgnoreCase)
                    || choice.Equals("RGB", StringComparison.OrdinalIgnoreCase)
                    || choice.Equals("CMYK", StringComparison.OrdinalIgnoreCase)
                    || choice.Equals("DeviceRGB", StringComparison.OrdinalIgnoreCase));

            if (key.Equals("PageSize", StringComparison.OrdinalIgnoreCase) || key.Equals("media", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var choice in choices)
                    if (ParsePaper(choice) is { } paper && !papers.Any(existing => existing.PaperName == paper.PaperName)) papers.Add(paper);
            }

            if (key.Equals("Resolution", StringComparison.OrdinalIgnoreCase) || key.Equals("printer-resolution", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var choice in choices)
                {
                    var match = ResolutionPattern.Match(choice);
                    if (!match.Success) continue;
                    var x = int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
                    var y = match.Groups["y"].Success ? int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture) : x;
                    if (x > 0 && y > 0 && !resolutions.Any(existing => existing.X == x && existing.Y == y))
                        resolutions.Add(new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = x, Y = y });
                }
            }
        }

        return new PrinterCapabilities(duplex, color, [.. papers], [.. resolutions]);
    }

    private static PaperSize? ParsePaper(string choice)
    {
        var token = choice.Split('/')[0];
        var match = SizePattern.Match(token);
        int width;
        int height;
        if (match.Success)
        {
            var multiplier = match.Groups["unit"].Value == "mm" ? 100d / 25.4d : 100d;
            width = (int)Math.Round(double.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture) * multiplier);
            height = (int)Math.Round(double.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture) * multiplier);
        }
        else
        {
            (width, height) = token.ToUpperInvariant() switch
            {
                "A4" => (827, 1169),
                "LETTER" or "USLETTER" => (850, 1100),
                "LEGAL" or "USLEGAL" => (850, 1400),
                "A3" => (1169, 1654),
                "A5" => (583, 827),
                _ => (0, 0),
            };
        }

        if (width <= 0 || height <= 0) return null;
        var kind = token.ToUpperInvariant() switch
        {
            "A4" or "ISO_A4_210X297MM" => PaperKind.A4,
            "LETTER" or "USLETTER" or "NA_LETTER_8.5X11IN" => PaperKind.Letter,
            "LEGAL" or "USLEGAL" or "NA_LEGAL_8.5X14IN" => PaperKind.Legal,
            "A3" or "ISO_A3_297X420MM" => PaperKind.A3,
            "A5" or "ISO_A5_148X210MM" => PaperKind.A5,
            _ => PaperKind.Custom,
        };
        return new PaperSize(token, width, height) { RawKind = (int)kind };
    }
}
