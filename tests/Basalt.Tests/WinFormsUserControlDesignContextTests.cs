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
