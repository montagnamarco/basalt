using Basalt.Core.Commands;

namespace Basalt.Tests;

/// <summary>
/// The command that shows what a template turns into.
///
/// Razor's tooling exposes the generated document, and for the same reason:
/// when an error lands on a line nobody wrote, that file is the only place
/// the answer is.
/// </summary>
public sealed class GeneratedCodeCommandTests
{
    [Fact]
    public void TheCommandExists()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);

        Assert.NotNull(registry.ById(IdeCommands.ViewGeneratedCode));
    }

    [Fact]
    public void ItHasAShortcut()
    {
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);

        Assert.NotNull(registry.GestureFor(IdeCommands.ViewGeneratedCode));
    }

    [Theory]
    [InlineData(KeyboardScheme.Basalt)]
    [InlineData(KeyboardScheme.VisualStudio)]
    [InlineData(KeyboardScheme.VisualStudioCode)]
    public void ItsShortcutIsFreeInEveryScheme(KeyboardScheme scheme)
    {
        var registry = IdeCommands.CreateRegistry(scheme);

        var gesture = registry.GestureFor(IdeCommands.ViewGeneratedCode);

        Assert.NotNull(gesture);
        Assert.Empty(registry.Conflicts(gesture, IdeCommands.ViewGeneratedCode));
    }
}
