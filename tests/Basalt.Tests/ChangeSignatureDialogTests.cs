using Avalonia.Headless.XUnit;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The dialog that says what a signature should become.
///
/// It produces the change the refactoring is given, so what it gets wrong the
/// refactoring would apply faithfully.
/// </summary>
public sealed class ChangeSignatureDialogTests
{
    private static ChangeSignatureDialog WithThree() =>
        new([("first", "Integer"), ("second", "String"), ("third", "Boolean")]);

    [AvaloniaFact]
    public void StartsWithTheParametersAsTheyAre()
    {
        var change = WithThree().CurrentForTests();

        Assert.Equal([0, 1, 2], change.Order);
        Assert.Empty(change.Added);
    }

    [AvaloniaFact]
    public void MovesAParameterUp()
    {
        var dialog = WithThree();

        dialog.MoveForTests(1, -1);

        Assert.Equal([1, 0, 2], dialog.CurrentForTests().Order);
    }

    [AvaloniaFact]
    public void MovesAParameterDown()
    {
        var dialog = WithThree();

        dialog.MoveForTests(0, 1);

        Assert.Equal([1, 0, 2], dialog.CurrentForTests().Order);
    }

    [AvaloniaFact]
    public void LeavesTheFirstAndLastWhereTheyAre()
    {
        // Moving past either end would drop the parameter off the list.
        var dialog = WithThree();

        dialog.MoveForTests(0, -1);
        dialog.MoveForTests(2, 1);

        Assert.Equal([0, 1, 2], dialog.CurrentForTests().Order);
    }

    [AvaloniaFact]
    public void RemovesAParameter()
    {
        var dialog = WithThree();

        dialog.RemoveForTests(1);

        Assert.Equal([0, 2], dialog.CurrentForTests().Order);
    }

    [AvaloniaFact]
    public void KeepsTrackOfWhichParameterIsWhichAfterMoving()
    {
        // The order is by original index, so removing after a move has to
        // remove the parameter the user pointed at rather than a position.
        var dialog = WithThree();

        dialog.MoveForTests(2, -1);
        dialog.RemoveForTests(1);

        Assert.Equal([0, 2], dialog.CurrentForTests().Order);
    }

    [AvaloniaFact]
    public void GivesNothingBackUntilItIsAccepted()
    {
        // Result stays null until Apply, so a dialog dismissed changes nothing.
        Assert.Null(WithThree().Result);
    }
}
