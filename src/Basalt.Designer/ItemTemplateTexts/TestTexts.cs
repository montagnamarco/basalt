namespace Basalt.Designer;

/// <summary>
/// One test class for each of the frameworks. The templates assume the
/// project references the framework's package; when it does not, Add New Item
/// says so, and adds the file all the same.
/// </summary>
internal static class TestTexts
{
    public static string XUnit(string name) => $"""
        Imports Xunit

        Public Class {name}
            <Fact>
            Public Sub Test1()
                Assert.True(True)
            End Sub
        End Class

        """;

    public static string NUnit(string name) => $"""
        Imports NUnit.Framework

        <TestFixture>
        Public Class {name}
            <Test>
            Public Sub Test1()
                Assert.Pass()
            End Sub
        End Class

        """;

    public static string MSTest(string name) => $"""
        Imports Microsoft.VisualStudio.TestTools.UnitTesting

        <TestClass>
        Public Class {name}
            <TestMethod>
            Public Sub Test1()
                Assert.IsTrue(True)
            End Sub
        End Class

        """;
}
