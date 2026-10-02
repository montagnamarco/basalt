namespace Basalt.Forms.Tests;

/// <summary>
/// Tests that read or change state the whole process shares - the clipboard,
/// and the active form SendKeys types into - run on their own, after the
/// parallel ones: another class writing the clipboard or opening a form at
/// the same moment would otherwise make them fail at random.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedStateCollection
{
    public const string Name = "Process-wide state";
}
