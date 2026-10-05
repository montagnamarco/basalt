using Avalonia;
using Avalonia.Styling;

namespace Basalt.Tests;

/// <summary>
/// Sets the application's theme variant for one test and puts back the one it
/// found. The headless application is shared by every test in the run, so a
/// test that leaves Dark behind makes the next one that assumes Light fail
/// depending on the order they run in.
/// </summary>
public sealed class ThemeScope : IDisposable
{
    private readonly ThemeVariant? _previous;

    public ThemeScope(ThemeVariant variant)
    {
        _previous = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = variant;
    }

    public void Dispose() => Application.Current!.RequestedThemeVariant = _previous;
}
