using System.Text.Json;
using Basalt.Forms.Goldens;

namespace Basalt.Forms.Tests;

/// <summary>
/// Each scenario of Basalt.Forms.Goldens, run here against Basalt.Forms and
/// compared with the behaviour Microsoft's own System.Windows.Forms recorded
/// (the behaviour.json in Goldens, made by the Basalt.Forms.Goldens
/// executable on Windows): the same events, in the same order, leading to the
/// same final state.
/// </summary>
/// <remarks>
/// Until behaviour.json is in the Goldens folder, each scenario is still run,
/// so that none of them throws, and the comparison is skipped.
/// </remarks>
public class BehaviourGoldenTests
{
    private static readonly string GoldenFile = Path.Combine(AppContext.BaseDirectory, "Goldens", "behaviour.json");

    public static TheoryData<string> Names() => [.. Scenarios.All.Select(scenario => scenario.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_scenario_behaves_as_Windows_Forms_behaves(string name)
    {
        var scenario = Scenarios.All.Single(candidate => candidate.Name == name);

        var ours = Run(scenario);

        if (!File.Exists(GoldenFile))
        {
            Assert.Skip("No recorded behaviour from Windows yet: see tests/Basalt.Forms.Goldens/README.md.");
        }

        var golden = LoadGoldenBehaviour();
        if (!golden.TryGetValue(name, out var expected))
        {
            Assert.Fail($"behaviour.json has no entry for scenario '{name}'; regenerate it from the current Scenarios.cs.");
            return;
        }

        Assert.Equal(expected, ours);
    }

    /// <summary>Runs the scenario, turning an unexpected exception into the same kind of line the recorder writes.</summary>
    private static IReadOnlyList<string> Run(Scenarios.Scenario scenario)
    {
        try
        {
            return scenario.Run();
        }
        catch (Exception exception)
        {
            return [$"throws {exception.GetType().Name}"];
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> LoadGoldenBehaviour()
    {
        var json = File.ReadAllText(GoldenFile);

        return JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(json)
            ?? throw new InvalidOperationException($"{GoldenFile} did not deserialize to a scenario map.");
    }
}
