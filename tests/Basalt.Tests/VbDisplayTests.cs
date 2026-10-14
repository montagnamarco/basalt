using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>netcoredbg's C# spelling of names, types, values and frames, as a Visual Basic user reads them.</summary>
public class VbDisplayTests
{
    [Theory]
    [InlineData("int", "Integer")]
    [InlineData("string", "String")]
    [InlineData("int[]", "Integer()")]
    [InlineData("int[,]", "Integer(,)")]
    [InlineData("int?", "Integer?")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List(Of Integer)")]
    [InlineData("System.Collections.Generic.Dictionary<string, int>", "System.Collections.Generic.Dictionary(Of String, Integer)")]
    [InlineData("System.DateTime", "Date")]
    [InlineData("Customer", "Customer")]
    public void WritesTypesAsVb(string csharp, string vb) => Assert.Equal(vb, VbDisplay.Type(csharp));

    [Theory]
    [InlineData("null", "Nothing")]
    [InlineData("true", "True")]
    [InlineData("false", "False")]
    [InlineData("{int[3]}", "{Length=3}")]
    [InlineData("{System.Collections.Generic.List<int>}", "{System.Collections.Generic.List(Of Integer)}")]
    [InlineData("{Customer}", "{Customer}")]
    [InlineData("\"Rossi\"", "\"Rossi\"")]
    [InlineData("42", "42")]
    [InlineData("{System.IndexOutOfRangeException}", "{System.IndexOutOfRangeException}")]
    public void WritesValuesAsVb(string csharp, string vb) => Assert.Equal(vb, VbDisplay.Value(csharp));

    [Fact]
    public void CallsThisMe() => Assert.Equal("Me", VbDisplay.Name("this"));

    [Theory]
    [InlineData("Program.Fail(Int32 n)", "Program.Fail(n As Integer)")]
    [InlineData("Program.Greet(String name, Int64 id)", "Program.Greet(name As String, id As Long)")]
    [InlineData("Program.Main()", "Program.Main()")]
    [InlineData("Program.VB$StateMachine_1_ComputeAsync.MoveNext()", "Program.ComputeAsync()")]
    [InlineData("Program._Closure$__._Lambda$__0-0()", "Program.Lambda()")]
    [InlineData("System.Threading.Thread.Sleep()", "System.Threading.Thread.Sleep()")]
    [InlineData("Program.F(Dictionary<String, Int32> map, Int32 n)", "Program.F(map As Dictionary(Of String, Integer), n As Integer)")]
    [InlineData("Odd(name", "Odd(name")]
    [InlineData("Program.Swap(ref Int32 a, ref Int32 b)", "Program.Swap(ByRef a As Integer, ByRef b As Integer)")]
    public void NamesFramesAsTheSourceDoes(string reported, string vb) => Assert.Equal(vb, VbDisplay.FrameName(reported));
}
