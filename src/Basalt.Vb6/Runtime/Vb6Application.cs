using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Basalt.Vb6.Runtime;

/// <summary>
/// Starts a converted Visual Basic 6 program the way Visual Basic 6 started it.
/// </summary>
/// <remarks>
/// Visual Basic 6 loaded the startup form and showed it, then ran until that
/// form closed. A converted project has no such step of its own, so the module
/// the converter writes calls <see cref="Run{TForm}"/> with the form to show.
///
/// The form is created only after Avalonia has initialised, because a window
/// cannot exist before the framework does. It becomes the main window, so
/// closing it ends the program, as it did in Visual Basic 6.
///
/// The windowing backend is not chosen here: it comes from the Avalonia.Desktop
/// package, which this project does not reference, so a program that starts
/// without it stops with Avalonia's "no windowing platform" error.
/// </remarks>
public static class Vb6Application
{
    /// <summary>
    /// Starts Avalonia, shows the startup form, and returns when the program ends.
    /// </summary>
    /// <param name="createStartupForm">Creates the form on the UI thread.</param>
    /// <param name="args">The command line, passed on to Avalonia.</param>
    public static void Run<TForm>(Func<TForm> createStartupForm, string[] args)
        where TForm : Vb6Form
    {
        AppBuilder.Configure(() => new Vb6Host(createStartupForm))
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The Avalonia application that owns the startup form.</summary>
    private sealed class Vb6Host : Application
    {
        private readonly Func<Vb6Form> _createStartupForm;

        public Vb6Host(Func<Vb6Form> createStartupForm) => _createStartupForm = createStartupForm;

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = _createStartupForm().Window;

            base.OnFrameworkInitializationCompleted();
        }
    }
}
