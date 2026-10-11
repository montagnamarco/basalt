using System.Text.Json.Nodes;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The collectible context the WinForms designer loads a project's own build output into: the
/// IDE's Forms types are shared with the project, the project's assemblies are its own, and the
/// context is collected once it is unloaded. Each assertion runs on a real build of a real VB
/// UserControl project (see <see cref="WinFormsUserControlProject"/>).
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsUserControlDesignContextTests : IAsyncLifetime
{
    private WinFormsUserControlProject? _project;
    private string _outputAssembly = null!;

    public async ValueTask InitializeAsync()
    {
        // Built only where the tests can run: each test skips the rest (see RequireUnix).
        if (OperatingSystem.IsWindows() || WindowsDesktopSamples.FindSources() is null) return;

        _project = WinFormsUserControlProject.Create();
        var build = await new MsBuildBuildService().BuildAsync(_project.ProjectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Diagnostics));
        _outputAssembly = Assert.IsType<string>(build.OutputAssemblyPath);
    }

    public ValueTask DisposeAsync()
    {
        _project?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void ProjectControlsDeriveFromTheFormsTypesTheIdeAlreadyHas()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            var gauge = context.ProjectAssembly.GetType("App.GaugeControl", throwOnError: true)!;

            // The identity the whole design depends on: the project's control is a Forms control of the
            // designer's own Forms assembly, not a second copy of it loaded for the project.
            Assert.Same(typeof(Forms.UserControl), gauge.BaseType);
            Assert.True(typeof(Forms.Control).IsAssignableFrom(gauge));
            Assert.Same(typeof(Forms.Control).Assembly, gauge.BaseType!.Assembly);
        });
    }

    /// <summary>
    /// The libraries are the ones the build's deps.json lists. A dll that is only copied into the output folder is neither
    /// read nor described, so it cannot show in the toolbox or fail the designer.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the libraries taken from the output folder's dll files, Unrelated.dll (which is not an assembly) is
    /// attempted, fails with "Bad IL format" and is named in FailedLibraries, so the DoesNotContain on FailedLibraries fails.
    /// </remarks>
    [Fact]
    public void AnUnrelatedDllInTheOutputFolderIsNotListedOrRead()
    {
        RequireUnix();
        var folder = Path.GetDirectoryName(_outputAssembly)!;
        File.WriteAllText(Path.Combine(folder, "Unrelated.dll"), "Not an assembly.");

        WithContext(_outputAssembly, context =>
        {
            Assert.DoesNotContain("Unrelated", context.ReferencedLibraries().Select(library => library.Name));
            Assert.DoesNotContain(context.FailedLibraries, failure => failure.Name == "Unrelated");
            Assert.Contains("SharedControls", context.ReferencedLibraries().Select(library => library.Name));
        });
    }

    /// <summary>
    /// A library whose types do not describe is recorded with its reason and listed with no controls. The other libraries
    /// and the project are described as usual, and the library's types still resolve, so a form that names one is shown.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the description contained only for the four exception types the designer caught before, the
    /// TypeInitializationException of BrokenLibraryControl's attribute escapes Open and the test fails with that exception.
    /// </remarks>
    [Fact]
    public void ALibraryThatDoesNotDescribeIsRecordedAndTheOthersAreStillDescribed()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            var failure = Assert.Single(context.FailedLibraries);
            Assert.Equal("BrokenLibrary", failure.Name);
            Assert.Contains("Static constructor failed on purpose.", failure.Message);

            Assert.Empty(context.ReferencedLibraries().Single(library => library.Name == "BrokenLibrary").Controls);
            Assert.Contains(context.ReferencedLibraries().Single(library => library.Name == "SharedControls").Controls,
                item => item.DisplayName == "LibraryControl");
            Assert.Contains(context.ProjectControls, item => item.DisplayName == "GaugeControl");
            Assert.Equal("Broken.BrokenLibraryControl", context.FindType("Broken.BrokenLibraryControl")?.FullName);
        });
    }

    /// <summary>
    /// A deps.json that does not parse, or is missing, leaves the project without libraries but does not fail the context:
    /// the reason is recorded under the file's name, and the project's own controls are still described.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the fallback in DependencyLibraryNames rethrowing instead of recording, Open throws the
    /// JsonReaderException and the test fails with it (measured).
    /// </remarks>
    [Fact]
    public void ADependencyListThatDoesNotParseOrIsMissingListsNoLibraryButKeepsTheProjectControls()
    {
        RequireUnix();
        var depsPath = Path.Combine(Path.GetDirectoryName(_outputAssembly)!, "App.deps.json");

        File.WriteAllText(depsPath, "{ not json");
        WithContext(_outputAssembly, context =>
        {
            Assert.Empty(context.ReferencedLibraries());
            Assert.Contains(context.ProjectControls, item => item.DisplayName == "GaugeControl");
            Assert.Contains(Localizer.Get(StringKeys.WinFormsDesignerDependencyListUnreadable, "App.deps.json", ""), context.DependencyListProblem);
            Assert.Empty(context.FailedLibraries);
        });

        File.Delete(depsPath);
        WithContext(_outputAssembly, context =>
        {
            Assert.Empty(context.ReferencedLibraries());
            Assert.Contains(context.ProjectControls, item => item.DisplayName == "GaugeControl");
            Assert.Equal(Localizer.Get(StringKeys.WinFormsDesignerDependencyListMissing, "App.deps.json"), context.DependencyListProblem);
        });
    }

    /// <summary>
    /// The list names only the runtime target the build was made for, and only the project's references and packages. A
    /// runtime pack entry whose dll is in the output, and the assets of another target, are not listed.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the type filter removed (every entry of every target is a library), the runtime pack
    /// Fake.Platform is listed and fails to read, so the DoesNotContain on the names and on FailedLibraries both fail.
    /// The filter keeps "reference": the fixture's project references are typed so in this build (measured: SharedControls
    /// and BrokenLibrary are "reference", App's own "project" entry is excluded by name).
    /// </remarks>
    [Fact]
    public void ARuntimePackOrAnotherTargetsAssetIsNotListed()
    {
        RequireUnix();
        var folder = Path.GetDirectoryName(_outputAssembly)!;
        var depsPath = Path.Combine(folder, "App.deps.json");
        var deps = JsonNode.Parse(File.ReadAllText(depsPath))!.AsObject();
        var runtimeTarget = deps["runtimeTarget"]!["name"]!.GetValue<string>();

        deps["libraries"]!.AsObject()["Fake.Platform/1.0.0"] = JsonNode.Parse("{\"type\":\"runtimepack\"}");
        deps["targets"]!.AsObject()[runtimeTarget]!.AsObject()["Fake.Platform/1.0.0"] = JsonNode.Parse("{\"runtime\":{\"Fake.Platform.dll\":{}}}");
        deps["libraries"]!.AsObject()["Other/1.0.0"] = JsonNode.Parse("{\"type\":\"package\"}");
        deps["targets"]!.AsObject()["net8.0-windows/linux-x64"] = JsonNode.Parse("{\"Other/1.0.0\":{\"runtime\":{\"Other.dll\":{}}}}");
        File.WriteAllText(depsPath, deps.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "Fake.Platform.dll"), "Not an assembly.");
        File.WriteAllText(Path.Combine(folder, "Other.dll"), "Not an assembly.");

        WithContext(_outputAssembly, context =>
        {
            var names = context.ReferencedLibraries().Select(library => library.Name).ToArray();
            Assert.DoesNotContain("Fake.Platform", names);
            Assert.DoesNotContain("Other", names);
            Assert.Contains("SharedControls", names);
            Assert.DoesNotContain(context.FailedLibraries, failure => failure.Name is "Fake.Platform" or "Other");
        });
    }

    /// <summary>
    /// A library that cannot be read is remembered for the context and released with it: after the context is unloaded
    /// the runtime collects it, so a library that failed to load does not keep the build's context alive.
    /// </summary>
    /// <remarks>
    /// Guard, not a red-proof: the failure is remembered as text, so clearing the cache on unload is not what keeps the
    /// context collectable. Removing the Clear() on Unloading still passes (measured), so this test does not catch that
    /// line's removal; it guards the release of a failed library's context as a whole.
    /// </remarks>
    [Fact]
    public void ALibraryThatFailsToLoadDoesNotKeepItsContextAlive()
    {
        RequireUnix();
        var folder = Path.GetDirectoryName(_outputAssembly)!;

        // A truncated image: the header is there and the rest is not, so the read fails inside the runtime's loader.
        File.WriteAllBytes(Path.Combine(folder, "SharedControls.dll"), [0x4D, 0x5A, 0x90]);

        var context = UseFailingLibraryAndUnload(_outputAssembly);
        Assert.True(IsCollected(context), "the context of a library that failed to load was kept alive after unload");
    }

    // Not inlined, so no local of the caller keeps the context or its types reachable.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference UseFailingLibraryAndUnload(string assemblyPath)
    {
        var context = WinFormsDesignAssemblyContext.Open(assemblyPath);
        Assert.Contains(context.FailedLibraries, failure => failure.Name == "SharedControls");
        context.Unload();
        return new WeakReference(context);
    }

    /// <summary>
    /// Opening the context reads each library once. Asking it for its libraries, its controls and its types afterwards reads
    /// nothing, so the designer's interface thread never waits for a file.
    /// </summary>
    /// <remarks>
    /// Red-proof: with ReferencedLibraries reading each library's file again, the count grows on the first call and the
    /// Assert.Equal on the count fails.
    /// </remarks>
    [Fact]
    public void TheLibrariesAreReadWhenTheContextIsOpenedAndNotAgain()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            // The project, SharedControls and BrokenLibrary.
            var reads = context.AssemblyReadsForTests;
            Assert.True(reads >= 3, $"Expected the project and two libraries to be read, but {reads} were.");

            for (var attempt = 0; attempt < 3; attempt++)
            {
                _ = context.ReferencedLibraries();
                _ = context.ProjectControls;
                _ = context.FindType("Widgets.LibraryControl");
                _ = context.FindType("App.NoSuchControl");
            }

            Assert.Equal(reads, context.AssemblyReadsForTests);
        });
    }

    /// <summary>
    /// The libraries of the output are the libraries the project references, and never the project itself or a host
    /// assembly the IDE already has. A library type resolves in the context and is designable through it.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the project's own name not excluded from the list, App is listed (its deps.json entry is its own
    /// runtime dll) and the DoesNotContain on App fails. System.Windows.Forms is not in this fixture's deps.json (the list
    /// is App, BrokenLibrary, Microsoft.VisualBasic.Forms, SharedControls and System.Windows.Extensions), so the host check
    /// is not reached through the listing here; ProjectControlsDeriveFromTheFormsTypesTheIdeAlreadyHas guards the identity.
    /// </remarks>
    [Fact]
    public void ReferencedLibrariesAreTheReferencedLibrariesNotTheProjectOrTheHosts()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            var names = context.ReferencedLibraries().Select(library => library.Name).ToArray();
            Assert.Contains("SharedControls", names);
            Assert.DoesNotContain("App", names);
            Assert.DoesNotContain("System.Windows.Forms", names);

            var libraryControl = context.FindType("Widgets.LibraryControl");
            Assert.NotNull(libraryControl);
            Assert.Same(context.ReferencedLibraries().Single(library => library.Name == "SharedControls").Assembly, libraryControl.Assembly);
            Assert.True(WinFormsDesignTypes.IsDesignable(libraryControl, context));
            Assert.False(WinFormsDesignTypes.IsDesignable(libraryControl, context: null));
        });
    }

    [Fact]
    public void ProjectTypesAreDesignableOnlyThroughTheirOwnContext()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            var gauge = context.ProjectAssembly.GetType("App.GaugeControl", throwOnError: true)!;

            Assert.True(context.Contains(gauge));
            Assert.True(WinFormsDesignTypes.IsDesignable(gauge, context));
            Assert.False(context.Contains(typeof(Forms.Button)));
            Assert.True(WinFormsDesignTypes.IsDesignable(typeof(Forms.Button), context));
            Assert.True(WinFormsDesignTypes.IsDesignable(typeof(Forms.Button), context: null));
            Assert.False(WinFormsDesignTypes.IsDesignable(gauge, context: null));
            Assert.False(WinFormsDesignTypes.IsDesignable(typeof(WinFormsUserControlDesignContextTests), context));
        });
    }

    [Fact]
    public void TheResolverFindsTheProjectsTypesOnlyWhenFormsDoesNotHaveTheName()
    {
        RequireUnix();
        WithContext(_outputAssembly, context =>
        {
            Assert.Equal(typeof(Forms.Button), WinFormsDesignTypes.Find("System.Windows.Forms.Button", context));
            Assert.Equal("App.GaugeControl", WinFormsDesignTypes.Find("App.GaugeControl", context)?.FullName);
            Assert.Null(WinFormsDesignTypes.Find("App.GaugeControl", context: null));
            Assert.Null(WinFormsDesignTypes.Find("App.NoSuchControl", context));
        });
    }

    [Fact]
    public void ProjectAssemblyCanBeOverwrittenWhileTheContextIsLoaded()
    {
        RequireUnix();

        // A copy of the output, so the test can overwrite the file the context was opened from.
        var folder = Directory.CreateTempSubdirectory("basalt-design-overwrite-").FullName;
        try
        {
            var copy = Path.Combine(folder, Path.GetFileName(_outputAssembly));
            File.Copy(_outputAssembly, copy);
            WithContext(copy, context =>
            {
                _ = context.ProjectAssembly.GetType("App.GaugeControl", throwOnError: true);

                // On Windows a loaded assembly is locked by its path; the next build writes the same file.
                // Passes on macOS and Linux as well, where the check says nothing about the lock.
                File.Copy(_outputAssembly, copy, overwrite: true);
            });
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(folder);
        }
    }

    /// <summary>
    /// After the designer has used a project control the way it does (an instance, its
    /// properties and events through TypeDescriptor), unloading the context lets the runtime
    /// collect it: a rebuild does not keep the previous build of the user's code alive.
    /// </summary>
    /// <remarks>
    /// Two leaks were measured and fixed. The context's own field holding the project
    /// assembly kept it rooted (cleared on Unloading), and TypeDescriptor's private per-type
    /// tables (ReflectTypeDescriptionProvider.s_propertyCache and _typeData,
    /// TypeDescriptor.s_defaultProviderInitialized) kept the control type
    /// (WinFormsDesignTimeCaches). Removing either fix makes this test fail.
    /// </remarks>
    [Fact]
    public void UnloadingTheContextLetsTheRuntimeCollectIt()
    {
        RequireUnix();
        var context = UseAndUnload(_outputAssembly);

        for (var attempt = 0; attempt < 30 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "the unloaded design context was never collected");
    }

    /// <summary>
    /// The same after the designer has described a control with properties whose types are built from the project's
    /// own types: a <c>List(Of Reading)</c> and a <c>Reading()</c>.
    /// </summary>
    /// <remarks>
    /// Reading each property's Converter, as the Properties window does, gave the constructed types a node in
    /// TypeDescriptor.s_providerTable whose provider's _typeData kept the context alive: the cleanup only walked the
    /// provider chain of object. With the walk limited to that chain again this test fails; WinFormsDesignTimeCaches
    /// now also walks the chains stored in s_providerTable.
    /// </remarks>
    [Fact]
    public void UnloadingTheContextLetsTheRuntimeCollectItAfterConstructedTypesOfItsOwnWereDescribed()
    {
        RequireUnix();
        var context = UseConstructedTypesAndUnload(_outputAssembly);

        Assert.True(IsCollected(context), "the unloaded design context was kept by a constructed type of the project");
    }

    // Not inlined, so no local of the caller keeps the context or its types reachable.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference UseConstructedTypesAndUnload(string assemblyPath)
    {
        var context = WinFormsDesignAssemblyContext.Open(assemblyPath);
        var readings = context.ProjectAssembly.GetType("App.ReadingsControl", throwOnError: true)!;
        using (var control = (Forms.Control)Activator.CreateInstance(readings)!)
        {
            // The designer describes the control's properties, whose types are constructed from the project's types.
            var properties = System.ComponentModel.TypeDescriptor.GetProperties(control).Cast<System.ComponentModel.PropertyDescriptor>().ToList();
            Assert.Contains(properties, property => property.Name == "Readings");

            // The Properties window also asks each property for its converter (expandable rows,
            // standard values): that is what makes TypeDescriptor cache the constructed types.
            foreach (var property in properties)
                _ = property.Converter.GetPropertiesSupported();
        }

        context.Unload();
        return new WeakReference(context);
    }

    private static bool IsCollected(WeakReference context)
    {
        for (var attempt = 0; attempt < 30 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return !context.IsAlive;
    }

    // Not inlined, so no local of the caller keeps the context or its types reachable.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference UseAndUnload(string assemblyPath)
    {
        var context = WinFormsDesignAssemblyContext.Open(assemblyPath);
        var gauge = context.ProjectAssembly.GetType("App.GaugeControl", throwOnError: true)!;
        using (var control = (Forms.Control)Activator.CreateInstance(gauge)!)
        {
            Assert.NotEmpty(System.ComponentModel.TypeDescriptor.GetProperties(control));
            Assert.NotEmpty(System.ComponentModel.TypeDescriptor.GetEvents(control));
        }

        context.Unload();
        return new WeakReference(context);
    }

    /// <summary>Runs the test with a context over the assembly, unloading it afterwards whatever happens.</summary>
    private static void WithContext(string assemblyPath, Action<WinFormsDesignAssemblyContext> test)
    {
        var context = WinFormsDesignAssemblyContext.Open(assemblyPath);
        try
        {
            test(context);
        }
        finally
        {
            context.Unload();
        }
    }

    private static void RequireUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
    }
}
