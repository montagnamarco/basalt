using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Verifies that the IDE's main window really loads: it catches XAML errors,
/// compiled binding errors and missing resources, which would otherwise only
/// show up when the application starts.
/// </summary>
public class ShellStartupTests
{
    [AvaloniaFact]
    public void LaFinestraPrincipaleSiCarica()
    {
        using var host = new TestWindow();
        var window = host.Window;

        Assert.NotNull(window.DataContext);
        Assert.IsType<MainWindowViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void IlDialogoNuovaFinestraSiCarica()
    {
        var dialog = new NewWindowDialog();

        Assert.NotNull(dialog.FindControl<TextBox>("NameBox"));
        Assert.NotNull(dialog.FindControl<TextBox>("NamespaceBox"));
    }

    [AvaloniaFact]
    public void IComandiPrincipaliSonoDisponibili()
    {
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.BuildCommand);
        Assert.NotNull(vm.RunCommand);
        Assert.NotNull(vm.StopRunCommand);
        Assert.NotNull(vm.SaveCommand);
        Assert.NotNull(vm.UndoCommand);
        Assert.NotNull(vm.RedoCommand);
    }

}
