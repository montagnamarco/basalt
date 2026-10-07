using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The Windows / Visual Studio dialog button convention, across every
/// dialog that can be built headlessly: the primary action (OK / Create /
/// Save / Apply / Delete…) sits to the left of Cancel, the row is
/// right-aligned, Enter triggers the primary action and Esc dismisses the
/// dialog.
///
/// <see cref="NewWindowDialog"/> (XAML) already follows the rule and is
/// covered by <c>NewWindowDialogTests</c>; this class covers the dialogs
/// built in code and the one other XAML dialog in scope here,
/// <see cref="ProjectPropertiesDialog"/>. <c>NewSolutionDialog</c> is out of
/// scope: another change is bringing it to the same rule separately.
/// </summary>
public sealed class DialogButtonOrderTests
{
    [AvaloniaFact]
    public void ProjectPropertiesDialogPutsSaveBeforeCancelRightAligned() =>
        AssertPrimaryBeforeCancelRightAligned(new ProjectPropertiesDialog());

    [AvaloniaFact]
    public void MakeExecutableDialogPutsMakeBeforeCancelRightAligned() =>
        AssertPrimaryBeforeCancelRightAligned(
            new MakeExecutableDialog("App", [], Path.GetTempPath()));

    [AvaloniaFact]
    public void BreakpointConditionDialogPutsOkBeforeCancelRightAligned() =>
        AssertPrimaryBeforeCancelRightAligned(new BreakpointConditionDialog());

    [AvaloniaFact]
    public void RefactoringPreviewDialogPutsApplyBeforeCancelRightAligned() =>
        AssertPrimaryBeforeCancelRightAligned(new RefactoringPreviewDialog());

    [AvaloniaFact]
    public void ChangeSignatureDialogPutsApplyBeforeCancelRightAligned() =>
        AssertPrimaryBeforeCancelRightAligned(
            new ChangeSignatureDialog([("value", "Integer")]));

    /// <summary>
    /// Every dialog in this file must pass; this proves the assertion itself
    /// would have caught the defect it guards against. Reintroducing the old
    /// "Cancel, then OK" order in <see cref="BreakpointConditionDialog"/> (by
    /// hand, once, then restored) turned this red before the fix, exactly as
    /// it should.
    /// </summary>
    [AvaloniaFact]
    public void CatchesAPrimaryActionPlacedAfterCancel()
    {
        var dialog = new Window
        {
            Width = 300,
            Height = 200
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "OK", IsDefault = true };

        // The defect this suite exists to catch: Cancel placed before the
        // primary action.
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancel, ok }
        };

        dialog.Content = row;

        var ex = Record.Exception(() => AssertPrimaryBeforeCancelRightAligned(dialog));

        Assert.NotNull(ex);
    }

    /// <summary>
    /// Finds the dialog's primary and Cancel buttons by their keyboard role
    /// (<see cref="Button.IsDefault"/>, <see cref="Button.IsCancel"/>) rather
    /// than by name or position, then checks the position: the primary
    /// action's left edge must sit to the left of Cancel's, and the pair
    /// must hug the dialog's own right edge rather than float in the middle
    /// of the window.
    /// </summary>
    private static void AssertPrimaryBeforeCancelRightAligned(Window dialog)
    {
        dialog.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();

            var primary = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault);
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);

            Assert.NotSame(primary, cancel);
            Assert.True(primary.IsDefault, "Enter must trigger the primary action.");
            Assert.True(cancel.IsCancel, "Esc must dismiss the dialog.");

            var primaryPoint = primary.TranslatePoint(default, dialog)!.Value;
            var cancelPoint = cancel.TranslatePoint(default, dialog)!.Value;

            Assert.True(
                primaryPoint.X < cancelPoint.X,
                "The primary action must sit to the left of Cancel, matching Windows dialog order.");

            var cancelRight = cancelPoint.X + cancel.Bounds.Width;

            Assert.True(
                cancelRight >= dialog.ClientSize.Width - 32,
                "The button row must be right-aligned.");
        }
        finally
        {
            dialog.Close();
        }
    }
}
