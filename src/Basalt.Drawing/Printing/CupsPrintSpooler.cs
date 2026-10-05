using System.Diagnostics;
using System.Globalization;

namespace System.Drawing.Printing;

internal static class CupsPrintSpooler
{
    internal static string[] BuildArguments(string pdfPath, PrinterSettings settings, string documentName)
    {
        var arguments = new List<string> { "-d", settings.PrinterName, "-t", documentName };

        if (settings.Copies > 1)
        {
            arguments.Add("-n");
            arguments.Add(settings.Copies.ToString(CultureInfo.InvariantCulture));
            arguments.Add("-o");
            arguments.Add($"collate={settings.Collate.ToString().ToLowerInvariant()}");
        }

        var sides = settings.Duplex switch
        {
            Duplex.Simplex => "one-sided",
            Duplex.Vertical => "two-sided-long-edge",
            Duplex.Horizontal => "two-sided-short-edge",
            _ => null,
        };
        if (sides is not null)
        {
            arguments.Add("-o");
            arguments.Add($"sides={sides}");
        }

        // The PDF MediaBox already carries each page's paper size and orientation.
        arguments.Add(pdfPath);
        return [.. arguments];
    }

    internal static void Submit(string pdfPath, PrinterSettings settings, string documentName)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("lp")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildArguments(pdfPath, settings, documentName))
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new IOException("The print spooler did not respond within 30 seconds.");
            }

            if (process.ExitCode != 0)
                throw new IOException($"The print spooler rejected the job (exit {process.ExitCode}): {stderr.Result.Trim()}");

            _ = stdout.Result;
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new IOException("The CUPS lp command is unavailable.", error);
        }
    }
}
