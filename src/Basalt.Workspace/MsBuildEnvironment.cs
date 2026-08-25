using Microsoft.Build.Locator;

namespace Basalt.Workspace;

/// <summary>
/// Registers the .NET SDK with MSBuildLocator. It must run exactly once and
/// before touching any MSBuild type or MSBuildWorkspace, otherwise loading the
/// MSBuild assemblies fails.
/// </summary>
public static class MsBuildEnvironment
{
    private static readonly Lock Gate = new();
    private static bool _initialized;

    public static VisualStudioInstance? Instance { get; private set; }

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;

            if (MSBuildLocator.IsRegistered)
            {
                _initialized = true;
                return;
            }

            // On macOS and Linux there is a single instance (the .NET SDK); on Windows
            // several SDKs and Visual Studio can coexist: the most recent one is taken.
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            if (instances.Count == 0)
                throw new InvalidOperationException(
                    "Nessuna installazione di MSBuild trovata. Installare .NET SDK 8.0 o successivo.");

            Instance = instances.OrderByDescending(i => i.Version).First();
            MSBuildLocator.RegisterInstance(Instance);
            _initialized = true;
        }
    }
}
