using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Basalt.Tests;

/// <summary>
/// Exercises the template engine and the compiler together. Source files alone
/// cannot prove that a client component is compiled into the browser assembly.
/// </summary>
[Collection("PackageBuild")]
public sealed class BlazorTemplateModesTests : IClassFixture<BlazorTemplateModesFixture>
{
    private readonly BlazorTemplateModesFixture _fixture;

    public BlazorTemplateModesTests(BlazorTemplateModesFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("None", false, false)]
    [InlineData("Server", true, false)]
    [InlineData("WebAssembly", false, true)]
    [InlineData("Auto", true, true)]
    public async Task FreshTemplateCompilesWithTheSelectedHostAndComponentArchitecture(
        string mode, bool usesServer, bool usesWebAssembly)
    {
        var name = "Mode" + mode;
        var output = Path.Combine(_fixture.Root, name);
        await _fixture.RunAsync("generate-" + mode,
            "new", "blazor", "--language", "VB", "--name", name,
            "--output", output, "--interactivity", mode,
            "--debug:custom-hive", _fixture.Hive);

        var projects = Directory.GetFiles(output, "*.vbproj", SearchOption.AllDirectories);
        Assert.Equal(usesWebAssembly ? 2 : 1, projects.Length);
        var hostProject = projects.Single(path => Path.GetFileName(path) == name + ".vbproj");
        var hostDirectory = Path.GetDirectoryName(hostProject)!;

        // A unique package version and a private restore cache ensure the build
        // consumes this working tree's generator, never an older installed copy.
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            var compilerPackage = document.Descendants("PackageReference")
                .Single(element => (string?)element.Attribute("Include") == "Basalt.Razor.Vb");
            compilerPackage.SetAttributeValue("Version", _fixture.PackageVersion);
            document.Save(project);
        }
        File.Copy(_fixture.NuGetConfig, Path.Combine(output, "nuget.config"));

        var target = usesWebAssembly ? Path.Combine(output, name + ".slnx") : hostProject;
        await _fixture.RunAsync("build-" + mode,
            "build", target, "--configuration", "Release", "--nologo",
            "-m:1", "-nr:false", "-p:NuGetAudit=false",
            "-p:RestorePackagesPath=" + Path.Combine(_fixture.Root, "restore-cache"));

        var hostAssembly = Path.Combine(hostDirectory, "bin", "Release", "net10.0", name + ".dll");
        using var hostStream = File.OpenRead(hostAssembly);
        using var hostPe = new PEReader(hostStream);
        var host = hostPe.GetMetadataReader();
        AssertComponent(host, name + ".Components", "App");
        AssertComponent(host, name + ".Components", "Routes");
        AssertComponent(host, name + ".Components.Pages", "Home");
        var hostCalls = MemberNames(host);
        Assert.Equal(usesServer, hostCalls.Contains("AddInteractiveServerComponents"));
        Assert.Equal(usesServer, hostCalls.Contains("AddInteractiveServerRenderMode"));
        Assert.Equal(usesWebAssembly, hostCalls.Contains("AddInteractiveWebAssemblyComponents"));
        Assert.Equal(usesWebAssembly, hostCalls.Contains("AddInteractiveWebAssemblyRenderMode"));
        Assert.Equal(usesWebAssembly, hostCalls.Contains("AddAdditionalAssemblies"));

        var hostReferences = host.AssemblyReferences
            .Select(handle => host.GetString(host.GetAssemblyReference(handle).Name)).ToArray();
        Assert.Equal(usesWebAssembly, hostReferences.Contains(name + ".Client"));

        if (usesWebAssembly)
        {
            Assert.DoesNotContain(host.TypeDefinitions, handle =>
                host.GetString(host.GetTypeDefinition(handle).Name) == "Counter");
            var clientProject = projects.Single(path => Path.GetFileName(path) == name + ".Client.vbproj");
            var clientAssembly = Path.Combine(Path.GetDirectoryName(clientProject)!,
                "bin", "Release", "net10.0", name + ".Client.dll");
            using var clientStream = File.OpenRead(clientAssembly);
            using var clientPe = new PEReader(clientStream);
            AssertCounter(clientPe.GetMetadataReader(), name + ".Client.Pages",
                mode == "Auto" ? "InteractiveAuto" : "InteractiveWebAssembly");
        }
        else if (usesServer)
        {
            AssertCounter(host, name + ".Components.Pages", "InteractiveServer");
        }
        else
        {
            Assert.DoesNotContain(host.TypeDefinitions, handle =>
                host.GetString(host.GetTypeDefinition(handle).Name) == "Counter");
            Assert.DoesNotContain(hostCalls, call => call.StartsWith("get_Interactive", StringComparison.Ordinal));
        }
    }

    private static TypeDefinition AssertComponent(MetadataReader reader, string componentNamespace, string name)
    {
        var handle = Assert.Single(reader.TypeDefinitions, handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Name) == name && reader.GetString(type.Namespace) == componentNamespace;
        });
        var component = reader.GetTypeDefinition(handle);
        Assert.Equal(HandleKind.TypeReference, component.BaseType.Kind);
        var baseType = reader.GetTypeReference((TypeReferenceHandle)component.BaseType);
        Assert.Equal("Microsoft.AspNetCore.Components", reader.GetString(baseType.Namespace));
        Assert.Equal("ComponentBase", reader.GetString(baseType.Name));
        Assert.Contains(component.GetMethods(), method =>
            reader.GetString(reader.GetMethodDefinition(method).Name) == "BuildRenderTree");
        return component;
    }

    private static void AssertCounter(MetadataReader reader, string componentNamespace, string renderMode)
    {
        var counter = AssertComponent(reader, componentNamespace, "Counter");
        Assert.Contains(counter.GetFields(), field =>
            reader.GetString(reader.GetFieldDefinition(field).Name) == "currentCount");
        Assert.Contains(counter.GetMethods(), method =>
            reader.GetString(reader.GetMethodDefinition(method).Name) == "IncrementCount");
        Assert.Contains("get_" + renderMode, MemberNames(reader));
        Assert.Single(counter.GetCustomAttributes(), handle =>
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MethodDefinition) return false;
            var constructor = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            var attributeType = reader.GetTypeDefinition(constructor.GetDeclaringType());
            if (attributeType.BaseType.Kind != HandleKind.TypeReference) return false;
            var baseType = reader.GetTypeReference((TypeReferenceHandle)attributeType.BaseType);
            return reader.GetString(baseType.Namespace) == "Microsoft.AspNetCore.Components"
                && reader.GetString(baseType.Name) == "RenderModeAttribute";
        });
        Assert.Single(counter.GetCustomAttributes(), handle =>
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) return false;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Name) != "RouteAttribute") return false;
            var blob = reader.GetBlobReader(attribute.Value);
            return blob.ReadUInt16() == 1 && blob.ReadSerializedString() == "/counter";
        });
    }

    private static HashSet<string> MemberNames(MetadataReader reader) => reader.MemberReferences
        .Select(handle => reader.GetString(reader.GetMemberReference(handle).Name)).ToHashSet(StringComparer.Ordinal);
}

public sealed class BlazorTemplateModesFixture : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "basalt-blazor-modes", Guid.NewGuid().ToString("N"));
    public string Hive => Path.Combine(Root, "template-hive");
    public string NuGetConfig => Path.Combine(Root, "nuget.config");
    public string PackageVersion { get; } = "1.0.0-f12." + Guid.NewGuid().ToString("N");
    private string _repository = "";
    private string _logs = "";

    public async ValueTask InitializeAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        _repository = directory.FullName;
        Directory.CreateDirectory(Root);
        _logs = Path.Combine(_repository, "artifacts", "blazor-template-modes-" + Path.GetFileName(Root));
        Directory.CreateDirectory(_logs);
        var packages = Path.Combine(Root, "packages");
        await RunAsync("pack", "pack", Path.Combine(_repository, "src", "Basalt.Razor.Vb.Generator",
            "Basalt.Razor.Vb.Generator.csproj"), "--configuration", "Release", "--no-restore",
            "--output", packages, "--nologo", "-m:1", "-nr:false", "-p:PackageVersion=" + PackageVersion);

        // Reuse existing packages when available. A fresh machine can restore
        // the dependencies already declared by the template from NuGet into
        // this test's private cache without changing the user's package cache.
        var cachedPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var sources = new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "working-tree"), new XAttribute("value", packages)));
        if (Directory.Exists(cachedPackages))
            sources.Add(new XElement("add", new XAttribute("key", "existing-cache"), new XAttribute("value", cachedPackages)));
        sources.Add(new XElement("add", new XAttribute("key", "nuget"), new XAttribute("value", "https://api.nuget.org/v3/index.json")));
        new XDocument(new XElement("configuration", sources)).Save(NuGetConfig);

        // Registration belongs entirely to this disposable hive; the user's
        // dotnet new templates and settings are never changed.
        await RunAsync("register", "new", "install", Path.Combine(_repository, "templates", "content"),
            "--debug:custom-hive", Hive);
    }

    public async Task RunAsync(string logName, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var timedOut = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        var output = await standardOutput + await standardError;
        var log = Path.Combine(_logs, logName + ".log");
        await File.WriteAllTextAsync(log, output);
        Assert.False(timedOut, $"dotnet {logName} exceeded five minutes. Log: {log}\n{output}");
        Assert.True(process.ExitCode == 0, $"dotnet {logName} failed. Log: {log}\n{output}");
    }

    public ValueTask DisposeAsync()
    {
        // Root is constructed from the temp directory and a test-owned GUID;
        // deletion cannot target the repository or the user's template hive.
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
