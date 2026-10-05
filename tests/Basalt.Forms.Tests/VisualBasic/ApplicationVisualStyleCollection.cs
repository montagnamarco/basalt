namespace Basalt.Forms.Tests.VisualBasic;

// My.Application enables process-wide styles during Run. Keep those tests
// separate from raster tests that intentionally exercise the classic defaults.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApplicationVisualStyleCollection
{
    public const string Name = "Application visual styles";
}
