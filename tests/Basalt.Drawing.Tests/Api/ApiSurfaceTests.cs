using System.Drawing;
using System.Runtime.CompilerServices;

namespace Basalt.Drawing.Tests.Api;

/// <summary>
/// The assembly is a stand-in for Microsoft's System.Drawing.Common: it must
/// carry its name and never offer a member Microsoft's does not, or spell one
/// differently.
/// </summary>
public class ApiSurfaceTests
{
    private static readonly string Folder = Path.GetDirectoryName(ThisFile())!;
    private static readonly string ReferenceFile = Path.Combine(Folder, "System.Drawing.Common.reference.txt");
    private static readonly string PendingFile = Path.Combine(Folder, "System.Drawing.Common.pending.txt");

    private static readonly SortedSet<string> Reference = new(File.ReadAllLines(ReferenceFile), StringComparer.Ordinal);
    private static readonly SortedSet<string> Ours = ApiSurface.Describe(typeof(Bitmap).Assembly);

    [Fact]
    public void The_assembly_answers_to_the_name_binaries_ask_for()
    {
        var name = typeof(Bitmap).Assembly.GetName();

        Assert.Equal("System.Drawing.Common", name.Name);

        // A binary built for net10.0 asks for 10.0.0.0 and the runtime refuses a lower one.
        Assert.True(name.Version >= new Version(10, 0, 0, 0), $"Version {name.Version} is lower than a net10.0 binary asks for.");
    }

    /// <summary>
    /// The way a .resx holding an image, and any library built for .NET
    /// Framework, names the type: through System.Drawing, which the framework
    /// ships as a set of forwards to an assembly called System.Drawing.Common
    /// signed by Microsoft. The runtime has to follow that forward to this
    /// assembly, which carries the name and not the signature.
    /// </summary>
    [Fact]
    public void The_framework_forwards_System_Drawing_to_this_assembly()
    {
        var forwarded = Type.GetType(
            "System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            throwOnError: true);

        Assert.Same(typeof(Bitmap), forwarded);
    }

    [Fact]
    public void Nothing_is_public_that_Microsoft_does_not_have()
    {
        var extra = Ours.Except(Reference).ToList();

        Assert.True(extra.Count == 0,
            "Public surface that is not in Microsoft's reference assembly (a wrong signature shows up here):\n" + string.Join("\n", extra));
    }

    /// <summary>
    /// What is still missing is written down, and the list can only shrink:
    /// implementing a member without removing it from the list fails, and so
    /// does losing one that was there.
    /// </summary>
    /// <remarks>
    /// Run once with BASALT_UPDATE_API_BASELINE=1 to rewrite the list after
    /// implementing members, and read the diff before committing it.
    /// </remarks>
    [Fact]
    public void What_is_not_implemented_yet_is_exactly_the_pending_list()
    {
        var missing = Reference.Except(Ours).ToList();

        if (Environment.GetEnvironmentVariable("BASALT_UPDATE_API_BASELINE") == "1")
        {
            File.WriteAllLines(PendingFile, missing);
        }

        var pending = File.Exists(PendingFile) ? File.ReadAllLines(PendingFile) : [];

        var lost = missing.Except(pending).ToList();
        var done = pending.Except(missing).ToList();

        Assert.True(lost.Count == 0 && done.Count == 0,
            $"Missing but not in the pending list ({lost.Count}):\n{string.Join("\n", lost.Take(40))}\n\n" +
            $"In the pending list but implemented ({done.Count}):\n{string.Join("\n", done.Take(40))}");
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
