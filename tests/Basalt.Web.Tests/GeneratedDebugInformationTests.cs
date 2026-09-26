using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace Basalt.Web.Tests;

/// <summary>
/// The debugging information a real build leaves for a Visual Basic template.
/// </summary>
/// <remarks>
/// Read from the sample's own PDB, produced by the generator inside an
/// ordinary dotnet build: a writer can be right in isolation and the build
/// still call it without a file path, which is how components used to reach
/// the compiler with no #ExternalSource at all and no breakpoint in a
/// .vbrazor could bind.
/// </remarks>
public sealed class GeneratedDebugInformationTests
{
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    private static string SampleFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            return Path.Combine(directory.FullName, "samples", "Basalt.Sample.Mvc");
        }
    }

    /// <summary>Every document the PDB names, with its checksum.</summary>
    private static Dictionary<string, (Guid Algorithm, byte[] Hash)> Documents()
    {
        var assembly = typeof(Basalt.Sample.Mvc.Program).Assembly.Location;
        var pdb = Path.ChangeExtension(assembly, ".pdb");

        using var stream = File.OpenRead(pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();

        return reader.Documents
            .Select(reader.GetDocument)
            .ToDictionary(
                d => Path.GetFullPath(reader.GetString(d.Name)),
                d => (reader.GetGuid(d.HashAlgorithm), reader.GetBlobBytes(d.Hash)),
                StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Views", "Home", "Index.vbhtml")]
    [InlineData("Components", "Tally.vbrazor")]
    public void TheTemplateIsInThePdbWithTheChecksumOfTheFileOnDisk(params string[] relative)
    {
        var path = Path.GetFullPath(Path.Combine([SampleFolder, .. relative]));

        var documents = Documents();

        Assert.True(documents.ContainsKey(path),
            $"{path} is not a document of the PDB; it has:\n{string.Join("\n", documents.Keys)}");

        // Of the bytes on disk, BOM and line endings included: a checksum of
        // the decoded text would disagree with the file and a debugger would
        // refuse it, which is worse than having no checksum at all.
        var (algorithm, hash) = documents[path];

        Assert.Equal(Sha256, algorithm);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), hash);
    }
}
