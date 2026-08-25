using Basalt.Extensibility.Interpretation;
using Basalt.QuickBasic.Interpretation;

namespace Basalt.Tests;

/// <summary>
/// Running QuickBASIC programs.
///
/// What a program prints is the thing checked: it is what the user sees, and
/// it has to agree with what the compiled program prints.
/// </summary>
public sealed class QuickBasicInterpreterTests
{
    /// <summary>Runs a program and gives back everything it printed.</summary>
    private static async Task<string> RunAsync(string source, params string[] input)
    {
        var interpreter = new QuickBasicInterpreter();

        var errors = interpreter.Load(source);

        Assert.True(errors.Count == 0,
            $"The program did not read:\n{string.Join("\n", errors)}");

        var printed = new System.Text.StringBuilder();
        interpreter.Output += (_, text) => printed.Append(text);

        var answers = new Queue<string>(input);
        interpreter.InputRequested += (_, request) =>
            request.Response = answers.Count > 0 ? answers.Dequeue() : "";

        var stop = await interpreter.RunAsync();

        Assert.True(stop.Reason != StopReason.Error,
            $"The program stopped with an error: {stop.Message} (line {stop.Line})");

        return printed.ToString();
    }

    /// <summary>The printed text with QuickBASIC's spacing taken off.</summary>
    private static async Task<string> ValuesAsync(string source, params string[] input) =>
        string.Join("|", (await RunAsync(source, input))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()));

    [Fact]
    public async Task PrintsWhatItIsTold()
    {
        Assert.Equal("Hello", await ValuesAsync("PRINT \"Hello\""));
    }

    [Fact]
    public async Task PrintsANumberTheWayQuickBasicDoes()
    {
        // A leading space when positive, a trailing one always.
        Assert.Equal(" 42 \n", await RunAsync("PRINT 42"));
    }

    [Fact]
    public async Task WorksOutArithmetic()
    {
        Assert.Equal("7", await ValuesAsync("PRINT 1 + 2 * 3"));
    }

    [Fact]
    public async Task FollowsPrecedenceAndBrackets()
    {
        Assert.Equal("9", await ValuesAsync("PRINT (1 + 2) * 3"));
    }

    [Fact]
    public async Task KeepsAVariable()
    {
        Assert.Equal("10", await ValuesAsync("x = 10\nPRINT x"));
    }

    [Fact]
    public async Task JoinsStrings()
    {
        Assert.Equal("abcdef", await ValuesAsync("a$ = \"abc\"\nPRINT a$ + \"def\""));
    }

    [Fact]
    public async Task RunsAnIfThatHolds()
    {
        Assert.Equal("yes", await ValuesAsync("""
            x = 5
            IF x > 3 THEN
                PRINT "yes"
            ELSE
                PRINT "no"
            END IF
            """));
    }

    [Fact]
    public async Task RunsTheElseWhenItDoesNot()
    {
        Assert.Equal("no", await ValuesAsync("""
            x = 1
            IF x > 3 THEN
                PRINT "yes"
            ELSE
                PRINT "no"
            END IF
            """));
    }

    [Fact]
    public async Task CountsWithFor()
    {
        Assert.Equal("1|2|3", await ValuesAsync("""
            FOR i = 1 TO 3
                PRINT i
            NEXT i
            """));
    }

    [Fact]
    public async Task CountsBackwardsWithAStep()
    {
        Assert.Equal("3|2|1", await ValuesAsync("""
            FOR i = 3 TO 1 STEP -1
                PRINT i
            NEXT i
            """));
    }

    [Fact]
    public async Task SkipsALoopWhoseBoundsArePast()
    {
        Assert.Equal("done", await ValuesAsync("""
            FOR i = 5 TO 1
                PRINT i
            NEXT i
            PRINT "done"
            """));
    }

    [Fact]
    public async Task RunsAWhileLoop()
    {
        Assert.Equal("1|2|3", await ValuesAsync("""
            i = 1
            WHILE i <= 3
                PRINT i
                i = i + 1
            WEND
            """));
    }

    [Fact]
    public async Task CallsASubroutine()
    {
        Assert.Equal("in the sub", await ValuesAsync("""
            CALL Greet
            SUB Greet
                PRINT "in the sub"
            END SUB
            """));
    }

    [Fact]
    public async Task PassesArgumentsToASub()
    {
        Assert.Equal("7", await ValuesAsync("""
            CALL Show(7)
            SUB Show(n)
                PRINT n
            END SUB
            """));
    }

    [Fact]
    public async Task CallsAFunctionInsideAnExpression()
    {
        Assert.Equal("25", await ValuesAsync("""
            PRINT Square(5)
            FUNCTION Square(n)
                Square = n * n
            END FUNCTION
            """));
    }

    [Fact]
    public async Task UsesTheLibraryFunctions()
    {
        Assert.Equal("5|ABC|abc|3", await ValuesAsync("""
            PRINT LEN("hello")
            PRINT UCASE$("abc")
            PRINT LCASE$("ABC")
            PRINT LEN(LEFT$("abcdef", 3))
            """));
    }

    [Fact]
    public async Task CountsStringsFromOne()
    {
        // MID$ and INSTR count from one, as QuickBASIC does; counting from
        // zero is the mistake that makes every string program wrong.
        Assert.Equal("bc|1|3", await ValuesAsync("""
            PRINT MID$("abcd", 2, 2)
            PRINT INSTR("abc", "a")
            PRINT INSTR("abc", "c")
            """));
    }

    [Fact]
    public async Task ReadsAnArray()
    {
        Assert.Equal("30", await ValuesAsync("""
            DIM a(5)
            a(2) = 30
            PRINT a(2)
            """));
    }

    [Fact]
    public async Task GivesAnArrayAsManyElementsAsQuickBasicDoes()
    {
        // DIM a(5) is six elements, zero through five.
        Assert.Equal("0", await ValuesAsync("""
            DIM a(5)
            a(5) = 0
            PRINT a(5)
            """));
    }

    [Fact]
    public async Task SaysWhenAnIndexIsOutOfRange()
    {
        var interpreter = new QuickBasicInterpreter();
        interpreter.Load("DIM a(2)\na(9) = 1");

        var stop = await interpreter.RunAsync();

        Assert.Equal(StopReason.Error, stop.Reason);
        Assert.Contains("Subscript out of range", stop.Message ?? "", StringComparison.Ordinal);
        Assert.Equal(2, stop.Line);
    }

    [Fact]
    public async Task SaysWhenSomethingIsDividedByZero()
    {
        var interpreter = new QuickBasicInterpreter();
        interpreter.Load("x = 1 / 0");

        var stop = await interpreter.RunAsync();

        Assert.Equal(StopReason.Error, stop.Reason);
        Assert.Contains("Division by zero", stop.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsWhatIsTyped()
    {
        Assert.Equal("42", await ValuesAsync("INPUT n\nPRINT n", "42"));
    }

    [Fact]
    public async Task ComparesWithMinusOneForTrue()
    {
        // QuickBASIC's own answer for a comparison that holds.
        Assert.Equal("-1|0", await ValuesAsync("PRINT 1 = 1\nPRINT 1 = 2"));
    }

    [Fact]
    public async Task RunsSelectCase()
    {
        Assert.Equal("two", await ValuesAsync("""
            x = 2
            SELECT CASE x
                CASE 1
                    PRINT "one"
                CASE 2
                    PRINT "two"
                CASE ELSE
                    PRINT "other"
            END SELECT
            """));
    }

    [Fact]
    public async Task FallsToCaseElse()
    {
        Assert.Equal("other", await ValuesAsync("""
            x = 9
            SELECT CASE x
                CASE 1
                    PRINT "one"
                CASE ELSE
                    PRINT "other"
            END SELECT
            """));
    }

    [Fact]
    public async Task JumpsWithGoto()
    {
        Assert.Equal("after", await ValuesAsync("""
            GOTO Skip
            PRINT "skipped"
            Skip:
            PRINT "after"
            """));
    }

    [Fact]
    public async Task StopsAtEnd()
    {
        Assert.Equal("before", await ValuesAsync("""
            PRINT "before"
            END
            PRINT "after"
            """));
    }

    [Fact]
    public async Task PassesAVariableByReference()
    {
        // QuickBASIC passes by reference by default, and programs of the era
        // lean on it to give back more than one value.
        Assert.Equal("99", await ValuesAsync("""
            x = 1
            CALL Change(x)
            PRINT x
            SUB Change(n)
                n = 99
            END SUB
            """));
    }

    [Fact]
    public async Task LeavesTheCallerAloneWhenAnExpressionIsPassed()
    {
        // Only a plain variable is passed by reference; an expression has no
        // place to write back to.
        Assert.Equal("1", await ValuesAsync("""
            x = 1
            CALL Change(x + 0)
            PRINT x
            SUB Change(n)
                n = 99
            END SUB
            """));
    }

    [Fact]
    public async Task ExitsALoopEarly()
    {
        Assert.Equal("1|2", await ValuesAsync("""
            FOR i = 1 TO 5
                IF i > 2 THEN
                    EXIT FOR
                END IF
                PRINT i
            NEXT i
            """));
    }

    [Fact]
    public async Task RunsADoLoopUntilSomethingHolds()
    {
        Assert.Equal("1|2|3", await ValuesAsync("""
            i = 1
            DO
                PRINT i
                i = i + 1
            LOOP UNTIL i > 3
            """));
    }

    [Fact]
    public async Task RunsADoWhileLoop()
    {
        Assert.Equal("1|2|3", await ValuesAsync("""
            i = 1
            DO WHILE i <= 3
                PRINT i
                i = i + 1
            LOOP
            """));
    }

    [Fact]
    public async Task RoundsWhenAValueGoesIntoAnInteger()
    {
        // QuickBASIC rounds to the nearest rather than truncating, so 3.7 is
        // 4. Checked against the compiled program, which now agrees.
        Assert.Equal("4|3", await ValuesAsync("x% = 3.7\nPRINT x%\ny% = 3.2\nPRINT y%"));
    }

    [Fact]
    public async Task StopsAProgramThatLoopsForever()
    {
        // The one a user needs when they wrote the loop wrong.
        var interpreter = new QuickBasicInterpreter();
        interpreter.Load("""
            DO
                x = x + 1
            LOOP
            """);

        var run = interpreter.RunAsync();

        await Task.Delay(50);

        interpreter.Stop();

        var stop = await run;

        Assert.Equal(StopReason.Paused, stop.Reason);
    }

    [Fact]
    public async Task ComesBackFromAGosub()
    {
        Assert.Equal("before|inside|after", await ValuesAsync("""
            PRINT "before"
            GOSUB Helper
            PRINT "after"
            END
            Helper:
            PRINT "inside"
            RETURN
            """));
    }
}
