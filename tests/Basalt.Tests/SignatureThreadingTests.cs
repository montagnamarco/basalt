using System.Collections.Concurrent;
using System.Globalization;
using Basalt.Extensibility;
using Basalt.Workspace;
using Basalt.Workspace.Languages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

public sealed class SignatureThreadingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WarmSignatureRequestReadsRoslynDocumentationOffTheCallerThread(bool sendCurrentText)
    {
        var root = Directory.CreateTempSubdirectory("basalt-signature-thread-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "Probe.vbproj");
            var file = Path.Combine(root, "Program.vb");
            const string code = "Module Program\n Sub Main()\n  ExternalApi.Greet(1, 2)\n End Sub\nEnd Module\n";
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace></RootNamespace>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(file, code);

            using var service = new RoslynLanguageService();
            await service.OpenSolutionAsync(projectPath);
            var documentation = new ThreadRecordingDocumentationProvider();
            var library = VisualBasicCompilation.Create("SignatureApi",
                [VisualBasicSyntaxTree.ParseText("""
                    Public Class ExternalApi
                        Public Shared Sub Greet(first As Integer, second As Integer)
                        End Sub
                    End Class
                    """)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emitted = library.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var reference = MetadataReference.CreateFromImage(image.ToArray(), documentation: documentation);
            var solution = service.CurrentSolution!;
            Assert.True(solution.Workspace.TryApplyChanges(
                solution.AddMetadataReference(solution.ProjectIds.Single(), reference)));

            var position = code.IndexOf(", 2", StringComparison.Ordinal) + 2;
            // Keep the existing workspace text so its warmed syntax and semantic
            // model tasks complete synchronously, reproducing the UI-thread risk.
            await service.FindCallAsync(file, position, currentText: null);
            var warmed = service.FindCallAsync(file, position, currentText: null);
            Assert.True(warmed.IsCompletedSuccessfully);
            Assert.NotNull(await warmed);

            var provider = new RoslynSymbolDescriptionProvider(service);
            int callerThread = 0;
            var requested = new TaskCompletionSource<Task<SymbolDescriptionSet?>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = new Thread(() =>
            {
                callerThread = Environment.CurrentManagedThreadId;
                requested.SetResult(provider.DescribeCallAsync(
                    new LanguageDocument(file, sendCurrentText ? code : null!), position));
            });
            caller.Start();
            var result = await await requested.Task.WaitAsync(TimeSpan.FromSeconds(30));
            caller.Join();

            Assert.NotNull(result);
            var description = Assert.Single(result.Overloads);
            Assert.Contains("Greet", description.PlainSignature, StringComparison.Ordinal);
            Assert.Equal(1, description.ActiveParameter);
            Assert.Equal("Greets the caller.", description.Documentation);
            Assert.Equal("Second value.", description.Parameters[1].Documentation);
            Assert.NotEmpty(documentation.Threads);
            Assert.All(documentation.Threads, thread => Assert.NotEqual(callerThread, thread));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ThreadRecordingDocumentationProvider : DocumentationProvider
    {
        public ConcurrentQueue<int> Threads { get; } = new();

        protected override string GetDocumentationForSymbol(
            string documentationMemberID, CultureInfo preferredCulture,
            CancellationToken cancellationToken = default)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            return "<member><summary>Greets the caller.</summary><param name=\"second\">Second value.</param></member>";
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
