namespace Basalt.Tests;

/// <summary>
/// Tests that point BASALT_SETTINGS at a file of their own. The variable is
/// process-wide, so while one of them runs every other test would read and
/// write that file too; a non-parallel collection runs them with nothing else
/// active.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SettingsFileCollection
{
    public const string Name = "SettingsFile";
}
