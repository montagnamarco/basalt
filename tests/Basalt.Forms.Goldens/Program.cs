using System.Text.Json;
using Basalt.Forms.Goldens;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("The reference behaviour comes from Windows' own System.Windows.Forms: run this on Windows.");
    return 1;
}

var output = Path.Combine(AppContext.BaseDirectory, "goldens");
Directory.CreateDirectory(output);

var behaviour = new Dictionary<string, IReadOnlyList<string>>();

foreach (var scenario in Scenarios.All)
{
    try
    {
        behaviour[scenario.Name] = scenario.Run();
    }
    catch (Exception exception)
    {
        // A scenario that throws on real Windows is still a result: the test compares the
        // exception's type the same way it compares any other recorded line.
        behaviour[scenario.Name] = [$"throws {exception.GetType().Name}"];
    }

    Console.WriteLine(scenario.Name);
}

File.WriteAllText(
    Path.Combine(output, "behaviour.json"),
    JsonSerializer.Serialize(behaviour, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Written to {output}");

return 0;
