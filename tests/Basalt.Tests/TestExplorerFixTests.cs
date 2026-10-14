using System.Reflection;
using System.Xml.Linq;
using Basalt.Core.Services;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// Recognising the test projects of a solution from the project file.
/// </summary>
/// <remarks>
/// Red-proof, one defect at a time, against this file: detection by searching
/// the text for the test SDK package (the old rule) fails
/// RecognisesAProjectThatNamesTheMSTestSdk (no such text in an MSTest.Sdk
/// project) and IgnoresTheTestSdkWhenItIsOnlyInAComment (a comment matches).
/// </remarks>
public class TestProjectDetectionFixTests
{
    private static bool IsTest(string projectXml) =>
        TestProjectDetector.IsTestProject(XDocument.Parse(projectXml));

    [Fact]
    public void RecognisesAProjectThatNamesTheMSTestSdk()
    {
        // The MSTest SDK replaces the package, so the text "Microsoft.NET.Test.Sdk"
        // never appears: searching for it skipped these projects entirely.
        Assert.True(IsTest("""
            <Project Sdk="MSTest.Sdk/3.9.3">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """));
    }

    [Fact]
    public void RecognisesAProjectThatSetsIsTestProject()
    {
        Assert.True(IsTest("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>
            </Project>
            """));
    }

    [Fact]
    public void RecognisesTheTestSdkPackageReference()
    {
        Assert.True(IsTest("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void RecognisesAVisualBasicProjectWithTheOlderNamespace()
    {
        // Older VB project files declare the MSBuild namespace; the element
        // names are the same under it, so the match must not depend on it.
        Assert.True(IsTest("""
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003" ToolsVersion="15.0">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void IgnoresTheTestSdkWhenItIsOnlyInAComment()
    {
        // A comment that names the package is not a reference to it.
        Assert.False(IsTest("""
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- We used to reference Microsoft.NET.Test.Sdk here. -->
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """));
    }

    [Fact]
    public void ALibraryIsNotATestProject()
    {
        Assert.False(IsTest("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup>
            </Project>
            """));
    }

    [Fact]
    public void AProjectThatUsesThePlatformIsToldApartFromVSTest()
    {
        // The platform lists tests only by running their program, so discovery
        // takes a different route for these.
        var platform = XDocument.Parse("""<Project Sdk="MSTest.Sdk/3.9.3"><PropertyGroup /></Project>""");
        var vstest = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" /></ItemGroup>
            </Project>
            """);

        Assert.True(TestProjectDetector.UsesTestingPlatform(platform));
        Assert.False(TestProjectDetector.UsesTestingPlatform(vstest));
    }

    [Fact]
    public void FindsTheTestProjectsOfAFolderAndLeavesOutTheBuildOutput()
    {
        var root = Directory.CreateTempSubdirectory("basalt-detect-").FullName;

        try
        {
            WriteProject(root, "Tests", "<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />");
            WriteProject(root, "Library", "");

            // The same test project copied into a build folder must not count twice.
            Directory.CreateDirectory(Path.Combine(root, "Tests", "bin", "Debug"));
            File.WriteAllText(Path.Combine(root, "Tests", "bin", "Debug", "Copy.vbproj"),
                "<Project Sdk=\"MSTest.Sdk/3.9.3\" />");

            var found = TestProjectDetector.FindTestProjects(root).ToList();

            var project = Assert.Single(found);
            Assert.EndsWith("Tests.vbproj", project);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteProject(string root, string name, string items)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, name + ".vbproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{items}</ItemGroup></Project>");
    }
}

/// <summary>
/// Reading the names a runner lists, and giving each its class.
/// </summary>
/// <remarks>
/// Red-proof, one defect at a time: requiring a dot in each name (the old rule)
/// fails ReadsTheBareNamesMSTestAndNUnitList and ReadsTheNamesAPlatformProgramPrints;
/// dropping the class lookup in Qualify fails QualifiesABareNameWithItsClass;
/// dropping the report's class fails TheReportNamesAResultWithItsClass.
/// </remarks>
public class TestNameListingFixTests
{
    [Fact]
    public void ReadsTheBareNamesMSTestAndNUnitList()
    {
        // The VSTest adapters list the method alone, and the arguments of a case.
        const string output = """
            Sono disponibili i test seguenti:
                Passes
                Fails
                Adds (1,1,2)
                Adds(2,2,5)
            """;

        var names = DotnetTestService.ParseDiscovered(output);

        Assert.Equal(["Passes", "Fails", "Adds (1,1,2)", "Adds(2,2,5)"], names);
    }

    [Fact]
    public void ReadsTheNamesAPlatformProgramPrintsAndNotItsSummary()
    {
        const string output = """
            MSTest v3.9.3 (UTC 17/06/2025) [osx-arm64 - .NET 10.0.12]

              A
              B (1,2)

            Riepilogo individuazione test: trovati 2 test - sdk.dll (net10.0|arm64)
              duration: 53ms
            """;

        Assert.Equal(["A", "B (1,2)"], DotnetTestService.ParseProgramListing(output));
    }

    [Fact]
    public void ReadsTheProgramThatTheBuildPrintedLast()
    {
        // The references are built first; the project's own assembly comes last.
        const string output = """
              Lib -> /src/Lib/bin/Debug/net10.0/Lib.dll
              VbTests -> /src/VbTests/bin/Debug/net10.0/VbTests.dll
            """;

        Assert.Equal("/src/VbTests/bin/Debug/net10.0/VbTests.dll", DotnetTestService.AssemblyPathIn(output));
    }

    [Fact]
    public void KeepsNamesWithSpacesAndPathSeparators()
    {
        // A display name can have spaces, and a DataRow argument can be a path.
        // Dropping them lost real tests; the build and status lines are the ones to drop.
        const string output = """
            The following tests are available:
                Passes
                Opens when the file is empty
                Reads (C:\data\in.txt, 2)
                Reads (/tmp/a b.txt)
                Adds (a: 1.5, b: 2)
            """;

        Assert.Equal(
            ["Passes", "Opens when the file is empty", "Reads (C:\\data\\in.txt, 2)", "Reads (/tmp/a b.txt)", "Adds (a: 1.5, b: 2)"],
            DotnetTestService.ParseDiscovered(output));
    }

    [Theory]
    [InlineData("Adds (1,1,2)", "Adds")]
    [InlineData("Adds(2,2,5)", "Adds")]
    [InlineData("Sample.CalcTests.Adds(a: 1.5, b: 2)", "Sample.CalcTests.Adds")]
    [InlineData("Passes", "Passes")]
    public void TheMethodIsWhatComesBeforeTheArguments(string listed, string method)
    {
        Assert.Equal(method, TestNameResolver.MethodOf(listed));
    }

    [Fact]
    public void QualifiesABareNameWithItsClass()
    {
        // The methods of this very assembly, read from its metadata: the
        // test below is a [Fact] declared in TestNameListingFixTests.
        var methods = TestNameResolver.TestMethodsByName(Assembly.GetExecutingAssembly().Location);

        var names = TestNameResolver.Qualify(nameof(QualifiesABareNameWithItsClass), methods);

        Assert.Equal(
            $"Basalt.Tests.TestNameListingFixTests.{nameof(QualifiesABareNameWithItsClass)}",
            Assert.Single(names));
    }

    [Fact]
    public void QualifiesACaseWithTheClassOfItsMethod()
    {
        // "Adds (1,1,2)" is listed without its class; the case keeps its arguments.
        var methods = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Adds"] = ["Sample.CalcTests"]
        };

        Assert.Equal(["Sample.CalcTests.Adds (1,1,2)"], TestNameResolver.Qualify("Adds (1,1,2)", methods));
    }

    [Fact]
    public void KeepsAClassTheListingAlreadyNames()
    {
        // xUnit lists "Class.Method(...)"; it must not be given a second class.
        var methods = new Dictionary<string, IReadOnlyList<string>>
        {
            ["AddsTwo"] = ["Other.Class"]
        };

        Assert.Equal(["CalculatorTests.AddsTwo(a: 1)"],
            TestNameResolver.Qualify("CalculatorTests.AddsTwo(a: 1)", methods));
    }

    [Fact]
    public void KeepsBothClassesOfAMethodNameTheListingCollapsed()
    {
        // Two classes with a test of the same name are one line in the listing;
        // dropping either would hide a test, so both are kept.
        var methods = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Passes"] = ["Sample.First", "Sample.Second"]
        };

        Assert.Equal(["Sample.First.Passes", "Sample.Second.Passes"],
            TestNameResolver.Qualify("Passes", methods));
    }

    [Fact]
    public void LeavesANameItCannotFindAsItIs()
    {
        var methods = new Dictionary<string, IReadOnlyList<string>>();

        Assert.Equal(["Unknown"], TestNameResolver.Qualify("Unknown", methods));
    }

    [Fact]
    public void TheReportNamesAResultWithItsClass()
    {
        // MSTest and NUnit reports name the result "Adds (1,1,2)"; the report's
        // definition of the test names the class, which the discovery named too.
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestDefinitions>
                <UnitTest name="Adds (1,1,2)" id="11111111-0000-0000-0000-000000000001">
                  <TestMethod className="Sample.CalcTests" name="Adds" />
                </UnitTest>
              </TestDefinitions>
              <Results>
                <UnitTestResult testName="Adds (1,1,2)" testId="11111111-0000-0000-0000-000000000001"
                                outcome="Passed" duration="00:00:00.01" />
              </Results>
            </TestRun>
            """;

        var result = Assert.Single(DotnetTestService.ParseReport(trx));

        Assert.Equal("Sample.CalcTests.Adds (1,1,2)", result.FullyQualifiedName);
        Assert.Equal(TestOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void AResultThatAlreadyHasItsClassIsLeftAlone()
    {
        // xUnit reports "Class.Method(...)" already; a second class would double it.
        var classes = new Dictionary<string, string> { ["x"] = "CalculatorTests" };

        Assert.Equal("CalculatorTests.AddsTwo(a: 1)",
            DotnetTestService.QualifiedName("CalculatorTests.AddsTwo(a: 1)", "x", classes));
    }

    [Theory]
    [InlineData("Sample.CalcTests.Adds(1,2)", "FullyQualifiedName=Sample.CalcTests.Adds&Name=Adds\\(1,2\\)")]
    [InlineData("Adds (1,2)", "FullyQualifiedName=Adds&Name=Adds \\(1,2\\)")]
    [InlineData("Sample.CalcTests.Passes", "FullyQualifiedName=Sample.CalcTests.Passes")]
    public void ACaseIsFilteredByItsMethodAndItsDisplayName(string name, string expected)
    {
        // The runner's fully-qualified name of every case is the method's, so the
        // method alone runs all of them; the display name picks out one.
        Assert.Equal(expected, DotnetTestService.FilterFor([name]));
    }

    [Fact]
    public void TheMethodPartOfAFilterIsEscapedToo()
    {
        // "!" and "=" are operators in a filter; a method name holding them must be escaped
        // as the display name is, or the runner reads the filter as something else.
        Assert.Equal("FullyQualifiedName=Sample.A\\!B\\=C.One", DotnetTestService.FilterFor(["Sample.A!B=C.One"]));
        Assert.Equal(
            "FullyQualifiedName=Sample.A\\=B.Adds&Name=Adds\\(1\\)",
            DotnetTestService.FilterFor(["Sample.A=B.Adds(1)"]));
    }

    [Fact]
    public void TheWholeMethodsOfACaseWithAPipeOrAmpersandAreCutAtTheRealSeparators()
    {
        // The display names hold "|" and "&", which the filter escapes. Splitting at
        // every "|" cut the retry filter inside a name and ran nothing.
        var filter = DotnetTestService.FilterFor(["Sample.T.Adds(a|b)", "Sample.T.Adds(c&d)"]);

        Assert.Equal("FullyQualifiedName=Sample.T.Adds", DotnetTestService.WholeMethodsOf(filter));
    }

    [Fact]
    public void ASplitLeavesAnEscapedSeparatorInItsPart()
    {
        Assert.Equal(["a\\|b", "c"], DotnetTestService.SplitFilter("a\\|b|c", '|'));
    }

    [Fact]
    public void TheWholeMethodsOfACaseFilterDropTheDisplayNames()
    {
        // The fallback for a framework that matches no case by name: the method alone.
        Assert.Equal(
            "FullyQualifiedName=A.B.Adds|FullyQualifiedName=A.B.Other",
            DotnetTestService.WholeMethodsOf(
                "FullyQualifiedName=A.B.Adds&Name=Adds\\(1\\)|FullyQualifiedName=A.B.Adds&Name=Adds\\(2\\)|FullyQualifiedName=A.B.Other"));
    }

    [Fact]
    public void ReadsOnlyTheNamesIndentedLikeNamesNotTheBuildsLines()
    {
        // The build's lines are indented by two spaces, the names by four; prose
        // at two spaces is the build's, whatever it says.
        const string output = """
            The following tests are available:
              Restored /src/Tests.vbproj (in 1,14 sec).
                Passes
            """;

        Assert.Equal(["Passes"], DotnetTestService.ParseDiscovered(output));
    }

    [Fact]
    public void EachCaseOfAMethodIsItsOwnPartOfTheFilter()
    {
        var filter = DotnetTestService.FilterFor(["A.B.Adds(1)", "A.B.Adds(2)", "A.B.Other"]);

        Assert.Equal(
            "FullyQualifiedName=A.B.Adds&Name=Adds\\(1\\)|FullyQualifiedName=A.B.Adds&Name=Adds\\(2\\)|FullyQualifiedName=A.B.Other",
            filter);
    }
}
