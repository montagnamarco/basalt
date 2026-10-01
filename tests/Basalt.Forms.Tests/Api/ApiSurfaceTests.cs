using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Basalt.Drawing.Tests.Api;

namespace Basalt.Forms.Tests.Api;

/// <summary>
/// The two assemblies stand in for Microsoft's System.Windows.Forms and
/// System.Windows.Forms.Primitives: each must carry its name and never offer a
/// member Microsoft's does not, or spell one differently.
/// </summary>
/// <remarks>
/// What is not implemented yet is written in a pending list that can only
/// shrink. Run once with BASALT_UPDATE_API_BASELINE=1 to rewrite it after
/// implementing members, and read the diff before committing it.
/// </remarks>
public class ApiSurfaceTests
{
    private static readonly string Folder = Path.GetDirectoryName(ThisFile())!;

    public static TheoryData<string> Assemblies() =>
        ["System.Windows.Forms", "System.Windows.Forms.Primitives", "Microsoft.VisualBasic.Forms"];

    private static Assembly Load(string name) => name switch
    {
        "System.Windows.Forms" => typeof(Control).Assembly,
        "Microsoft.VisualBasic.Forms" => typeof(Microsoft.VisualBasic.AudioPlayMode).Assembly,
        _ => typeof(Padding).Assembly,
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void The_assembly_answers_to_the_name_binaries_ask_for(string name)
    {
        var identity = Load(name).GetName();

        Assert.Equal(name, identity.Name);
        Assert.True(identity.Version >= new Version(10, 0, 0, 0), $"Version {identity.Version} is lower than a net10.0 binary asks for.");
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Nothing_is_public_that_Microsoft_does_not_have(string name)
    {
        var reference = ReadReference(name);
        var extra = ApiSurface.Describe(Load(name)).Except(reference).ToList();

        Assert.True(extra.Count == 0,
            "Public surface that is not in Microsoft's reference assembly (a wrong signature shows up here):\n" + string.Join("\n", extra));
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void What_is_not_implemented_yet_is_exactly_the_pending_list(string name)
    {
        var pendingFile = Path.Combine(Folder, $"{name}.pending.txt");
        var missing = ReadReference(name).Except(ApiSurface.Describe(Load(name))).ToList();

        if (Environment.GetEnvironmentVariable("BASALT_UPDATE_API_BASELINE") == "1")
        {
            File.WriteAllLines(pendingFile, missing);
        }

        var pending = File.Exists(pendingFile) ? File.ReadAllLines(pendingFile) : [];

        var lost = missing.Except(pending).ToList();
        var done = pending.Except(missing).ToList();

        Assert.True(lost.Count == 0 && done.Count == 0,
            $"Missing but not in the pending list ({lost.Count}):\n{string.Join("\n", lost.Take(40))}\n\n" +
            $"In the pending list but implemented ({done.Count}):\n{string.Join("\n", done.Take(40))}");
    }

    private static SortedSet<string> ReadReference(string name) =>
        new(File.ReadAllLines(Path.Combine(Folder, $"{name}.reference.txt")), StringComparer.Ordinal);

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
