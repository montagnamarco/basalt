using Basalt.Core.Commands;

namespace Basalt.Tests;

/// <summary>
/// That every command the palette offers actually does something.
///
/// The palette lists everything in the registry and calls the shell with its
/// id. Ten of them fell through the switch and did nothing at all — chosen,
/// and the IDE simply carried on.
/// </summary>
public class CommandCoverageTests
{
    private static string Dispatcher()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName, "src", "Basalt.Shell", "MainWindow.axaml.cs");

            if (File.Exists(path)) return File.ReadAllText(path);

            directory = directory.Parent;
        }

        return "";
    }

    [Fact]
    public void EveryCommandIsHandledSomewhere()
    {
        var source = Dispatcher();

        Assert.SkipWhen(source.Length == 0, "The shell source was not found.");

        var registry = IdeCommands.CreateRegistry(KeyboardScheme.Basalt);

        // Matched on the id itself rather than on the constant's name: the
        // two do not always agree, and guessing the name is how an earlier
        // version of this reported a command that was handled all along.
        var unhandled = registry.All
            .Where(c => !source.Contains($"\"{c.Id}\"", StringComparison.Ordinal)
                     && !HandledByConstant(source, c.Id))
            .Select(c => c.Id)
            .ToList();

        Assert.True(unhandled.Count == 0,
            "These commands are offered and do nothing:\n"
            + string.Join("\n", unhandled));
    }

    /// <summary>
    /// Whether the dispatcher mentions the constant standing for an id.
    ///
    /// The constants are found in IdeCommands, so the name is read from there
    /// rather than guessed from the id.
    /// </summary>
    private static bool HandledByConstant(string source, string id)
    {
        foreach (var field in typeof(IdeCommands).GetFields())
        {
            if (field.GetValue(null) as string != id) continue;

            return source.Contains($"IdeCommands.{field.Name}", StringComparison.Ordinal);
        }

        return false;
    }
}
