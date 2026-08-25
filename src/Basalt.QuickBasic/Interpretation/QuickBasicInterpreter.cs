using Basalt.Extensibility.Interpretation;

namespace Basalt.QuickBasic.Interpretation;

/// <summary>
/// Runs a QuickBASIC program a statement at a time.
///
/// The stepping rules live in the base class, which knows nothing about
/// QuickBASIC; what is here is what a statement means. The program is walked
/// as a flat list of steps rather than by recursing through the tree, because
/// an interpreter that recurses cannot stop in the middle and be resumed,
/// which is the whole point of stepping.
/// </summary>
public sealed class QuickBasicInterpreter : InterpreterBase
{
    private readonly Random _random = new(1);

    /// <summary>The program, once it has been read.</summary>
    private Program? _program;

    /// <summary>Procedures by name, for calling them.</summary>
    private readonly Dictionary<string, Procedure> _procedures =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The variables of the top level, shared as QuickBASIC shares them.</summary>
    private Scope _globals = new("main");

    /// <summary>The calls in progress, innermost last.</summary>
    private readonly List<Frame> _frames = [];

    /// <summary>Where GOSUB should come back to.</summary>
    private readonly Stack<(Frame Frame, int Index)> _returns = new();

    public override int CurrentLine => Current?.CurrentLine ?? 0;

    protected override int CallDepth => _frames.Count;

    /// <summary>The frame being run.</summary>
    private Frame? Current => _frames.Count > 0 ? _frames[^1] : null;

    public override IReadOnlyList<InterpreterError> Load(string source, string? filePath = null)
    {
        _program = Parser.Parse(source);

        var errors = _program.Diagnostics
            .Select(d => new InterpreterError(d.Message, d.Line, d.Column))
            .ToList();

        if (errors.Count > 0) return errors;

        _procedures.Clear();

        foreach (var procedure in _program.Procedures)
            _procedures[procedure.Name] = procedure;

        _globals = new Scope("main");
        _frames.Clear();
        _returns.Clear();

        // The top level is a frame like any other, so stepping and the call
        // stack need no special case for it.
        _frames.Add(new Frame(_globals, Flatten(_program.Main), null));

        State = RunState.Ready;

        ResetStops();

        return [];
    }

    protected override bool ExecuteNextStatement(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        while (Current is { } frame)
        {
            if (frame.Index >= frame.Steps.Count)
            {
                // The end of a procedure: back to whoever called it.
                if (_frames.Count == 1) return false;

                LeaveProcedure();
                continue;
            }

            var step = frame.Steps[frame.Index];

            frame.Index++;
            frame.Scope.Line = step.Line;

            if (Run(step, frame)) return true;
        }

        return false;
    }

    /// <summary>
    /// Runs one step.
    ///
    /// Returns whether there is more to do, which is false only when the
    /// program has ended.
    /// </summary>
    private bool Run(Step step, Frame frame)
    {
        switch (step)
        {
            case Step.Execute execute:
                Execute(execute.Statement, frame);
                return true;

            case Step.Jump jump:
                frame.Index = jump.Target;
                return true;

            case Step.Gosub call:
                // Where to come back to is the step after this one, which
                // frame.Index already points at.
                _returns.Push((frame, frame.Index));
                frame.Index = call.Target;
                return true;

            case Step.JumpUnless conditional:
                if (!Evaluate(conditional.Condition, frame).IsTrue)
                    frame.Index = conditional.Target;
                return true;

            case Step.ForInit init:
                StartLoop(init, frame);
                return true;

            case Step.ForNext next:
                AdvanceLoop(next, frame);
                return true;

            case Step.End:
                _frames.Clear();
                return false;

            default:
                return true;
        }
    }

    /// <summary>Runs a statement that does not affect where we are.</summary>
    private void Execute(Statement statement, Frame frame)
    {
        switch (statement)
        {
            case Assignment assignment:
                Assign(assignment, frame);
                break;

            case Declaration declaration:
                Declare(declaration, frame);
                break;

            case PrintStatement print:
                Print(print, frame);
                break;

            case InputStatement input:
                Input(input, frame);
                break;

            case CallStatement call:
                CallProcedure(call.Name, call.Arguments, frame, call.Line);
                break;

            case ReturnStatement ret:
                DoReturn(ret, frame);
                break;

            case LabelStatement:
                // Nothing to do: a label is a place, not an action.
                break;
        }
    }

    private void Assign(Assignment assignment, Frame frame)
    {
        var value = Evaluate(assignment.Value, frame);

        switch (assignment.Target)
        {
            case VariableReference variable:
                SetVariable(frame.Scope, variable.Name, value);
                break;

            case IndexOrCall element:
                var array = FindArray(frame.Scope, element.Name)
                    ?? throw new InterpreterRuntimeException(
                        $"{element.Name} is not an array.", assignment.Line);

                var index = (int)Evaluate(element.Arguments[0], frame).Number;

                if (!array.Within(index))
                {
                    throw new InterpreterRuntimeException(
                        $"Subscript out of range: {element.Name}({index}).", assignment.Line);
                }

                array[index] = Convert(value, array.Type);
                break;
        }
    }

    private void Declare(Declaration declaration, Frame frame)
    {
        if (declaration.Bounds is { Count: > 0 } bounds)
        {
            var upper = (int)Evaluate(bounds[0], frame).Number;

            if (upper < 0)
            {
                throw new InterpreterRuntimeException(
                    "An array cannot have a negative bound.", declaration.Line);
            }

            frame.Scope.Arrays[declaration.Name] = new BasicArray(declaration.Type, upper);
            return;
        }

        frame.Scope.Variables[declaration.Name] = BasicValue.Default(declaration.Type);
    }

    private void Print(PrintStatement print, Frame frame)
    {
        var text = string.Concat(print.Values.Select(v => Evaluate(v, frame).Display()));

        ReportOutput(print.TrailingSemicolon ? text : text + Environment.NewLine);
    }

    private void Input(InputStatement input, Frame frame)
    {
        var answer = RequestInput(input.Prompt ?? "? ");

        // INPUT a, b splits what was typed on commas, as QuickBASIC does.
        var parts = answer.Split(',');

        for (var i = 0; i < input.Targets.Count; i++)
        {
            var text = i < parts.Length ? parts[i].Trim() : "";
            var name = input.Targets[i];

            var value = TypeSuffix.Of(name) == BasicType.String
                ? BasicValue.Of(text)
                : BasicValue.Of(
                    double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                        ? number
                        : 0,
                    TypeSuffix.Of(name));

            SetVariable(frame.Scope, name, value);
        }
    }

    /// <summary>Starts a FOR loop, working out its bounds once as QuickBASIC does.</summary>
    private void StartLoop(Step.ForInit init, Frame frame)
    {
        var from = Evaluate(init.From, frame);
        var to = Evaluate(init.To, frame).Number;
        var step = init.Step is null ? 1 : Evaluate(init.Step, frame).Number;

        SetVariable(frame.Scope, init.Variable, from);

        frame.Loops[init.Variable] = (to, step);

        // A loop whose bounds are already past does not run at all.
        if (Past(from.Number, to, step)) frame.Index = init.After;
    }

    private void AdvanceLoop(Step.ForNext next, Frame frame)
    {
        if (!frame.Loops.TryGetValue(next.Variable, out var bounds)) return;

        var current = ReadVariable(frame.Scope, next.Variable).Number + bounds.Step;

        SetVariable(frame.Scope, next.Variable,
            BasicValue.Of(current, TypeSuffix.Of(next.Variable)));

        if (!Past(current, bounds.To, bounds.Step)) frame.Index = next.Body;
    }

    /// <summary>Whether a loop counter has gone past its end.</summary>
    private static bool Past(double current, double to, double step) =>
        step >= 0 ? current > to : current < to;

    private void DoReturn(ReturnStatement statement, Frame frame)
    {
        // RETURN with no value comes back from a GOSUB; with one it is a
        // function giving its answer.
        if (statement.Value is null && _returns.Count > 0)
        {
            var (target, index) = _returns.Pop();

            target.Index = index;
            return;
        }

        if (statement.Value is { } value)
            frame.ReturnValue = Evaluate(value, frame);

        frame.Index = frame.Steps.Count;
    }

    /// <summary>Calls a procedure, or a library function, or reads an array.</summary>
    private BasicValue CallProcedure(
        string name, IReadOnlyList<Expression> arguments, Frame frame, int line)
    {
        if (_procedures.TryGetValue(name, out var procedure))
        {
            Enter(procedure, arguments, frame, line);

            // The result is only known once it has run; a call inside an
            // expression is handled by RunToCompletion below.
            return BasicValue.Default(procedure.ReturnType);
        }

        if (Library.Has(name))
        {
            var values = arguments.Select(a => Evaluate(a, frame)).ToList();

            return Library.Call(name, values, line, _random);
        }

        throw new InterpreterRuntimeException($"{name} is not defined.", line);
    }

    /// <summary>Pushes a frame for a call, binding its parameters.</summary>
    private void Enter(
        Procedure procedure, IReadOnlyList<Expression> arguments, Frame caller, int line)
    {
        if (_frames.Count > 256)
            throw new InterpreterRuntimeException("Too many nested calls.", line);

        var scope = new Scope(procedure.Name) { Line = procedure.Line };

        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            var parameter = procedure.Parameters[i];

            if (i >= arguments.Count)
            {
                scope.Variables[parameter.Name] = BasicValue.Default(parameter.Type);
                continue;
            }

            scope.Variables[parameter.Name] =
                Convert(Evaluate(arguments[i], caller), parameter.Type);

            // QuickBASIC passes by reference, so a plain variable passed in is
            // written back when the call ends.
            if (arguments[i] is VariableReference reference)
                scope.ByReference[parameter.Name] = (caller.Scope, reference.Name);
        }

        _frames.Add(new Frame(scope, Flatten(procedure.Body), procedure));
    }

    /// <summary>Pops a frame, writing back what was passed by reference.</summary>
    private void LeaveProcedure()
    {
        var frame = _frames[^1];

        _frames.RemoveAt(_frames.Count - 1);

        foreach (var (parameter, (scope, name)) in frame.Scope.ByReference)
        {
            if (frame.Scope.Variables.TryGetValue(parameter, out var value))
                SetVariable(scope, name, value);
        }

        if (frame.Procedure is { IsFunction: true } procedure && Current is { } caller)
        {
            // A QuickBASIC function gives its answer by assigning to its own
            // name, so that is where the value is looked for; an explicit
            // RETURN takes priority where a program uses one.
            var named = frame.Scope.Variables.TryGetValue(procedure.Name, out var assigned)
                ? assigned
                : (BasicValue?)null;

            caller.LastCallResult =
                frame.ReturnValue ?? named ?? BasicValue.Default(procedure.ReturnType);
        }
    }

    // Expressions

    /// <summary>Works out what an expression comes to.</summary>
    private BasicValue Evaluate(Expression expression, Frame frame) => expression switch
    {
        NumberLiteral number => BasicValue.Of(number.Value, number.Type),

        StringLiteral text => BasicValue.Of(text.Value),

        VariableReference variable => ReadVariable(frame.Scope, variable.Name),

        Unary unary => ApplyUnary(unary, frame),

        Binary binary => ApplyBinary(binary, frame),

        IndexOrCall call => ReadIndexOrCall(call, frame),

        _ => BasicValue.Of(0)
    };

    private BasicValue ApplyUnary(Unary unary, Frame frame)
    {
        var value = Evaluate(unary.Operand, frame);

        return unary.Operator switch
        {
            "-" => BasicValue.Of(-value.Number, value.Type),
            "+" => value,
            "NOT" => BasicValue.Of(~(int)value.Number, BasicType.Integer),
            _ => value
        };
    }

    private BasicValue ApplyBinary(Binary binary, Frame frame)
    {
        var left = Evaluate(binary.Left, frame);
        var right = Evaluate(binary.Right, frame);

        // Joining strings is the one place + means something else.
        if (binary.Operator == "+" && left.IsString && right.IsString)
            return BasicValue.Of(left.Written() + right.Written());

        if (left.IsString != right.IsString && IsArithmetic(binary.Operator))
        {
            throw new InterpreterRuntimeException(
                "A string and a number cannot be combined.", binary.Line);
        }

        if (IsComparison(binary.Operator)) return Compare(binary.Operator, left, right);

        var a = left.Number;
        var b = right.Number;
        var type = Wider(left.Type, right.Type);

        return binary.Operator switch
        {
            "+" => BasicValue.Of(a + b, type),
            "-" => BasicValue.Of(a - b, type),
            "*" => BasicValue.Of(a * b, type),

            "/" => b == 0
                ? throw new InterpreterRuntimeException("Division by zero.", binary.Line)
                : BasicValue.Of(a / b, BasicType.Double),

            "\\" => b == 0
                ? throw new InterpreterRuntimeException("Division by zero.", binary.Line)
                : BasicValue.Of(Math.Truncate((long)a / (double)(long)b), BasicType.Long),

            "MOD" => b == 0
                ? throw new InterpreterRuntimeException("Division by zero.", binary.Line)
                : BasicValue.Of((long)a % (long)b, BasicType.Long),

            "^" => BasicValue.Of(Math.Pow(a, b), BasicType.Double),

            "AND" => BasicValue.Of((long)a & (long)b, BasicType.Long),
            "OR" => BasicValue.Of((long)a | (long)b, BasicType.Long),
            "XOR" => BasicValue.Of((long)a ^ (long)b, BasicType.Long),

            _ => throw new InterpreterRuntimeException(
                $"{binary.Operator} is not an operator here.", binary.Line)
        };
    }

    private static bool IsArithmetic(string op) =>
        op is "-" or "*" or "/" or "\\" or "MOD" or "^";

    private static bool IsComparison(string op) =>
        op is "=" or "<>" or "<" or ">" or "<=" or ">=";

    /// <summary>Compares two values, giving -1 for true as QuickBASIC does.</summary>
    private static BasicValue Compare(string op, BasicValue left, BasicValue right)
    {
        var order = left.IsString || right.IsString
            ? string.CompareOrdinal(left.Written(), right.Written())
            : left.Number.CompareTo(right.Number);

        return BasicValue.FromBool(op switch
        {
            "=" => order == 0,
            "<>" => order != 0,
            "<" => order < 0,
            ">" => order > 0,
            "<=" => order <= 0,
            ">=" => order >= 0,
            _ => false
        });
    }

    /// <summary>The type that holds both, so INTEGER + DOUBLE is a DOUBLE.</summary>
    private static BasicType Wider(BasicType a, BasicType b) =>
        (BasicType)Math.Max((int)Rank(a), (int)Rank(b)) switch
        {
            var rank => rank
        };

    private static BasicType Rank(BasicType type) => type;

    /// <summary>An array element, a call, or a library function.</summary>
    private BasicValue ReadIndexOrCall(IndexOrCall call, Frame frame)
    {
        if (FindArray(frame.Scope, call.Name) is { } array)
        {
            var index = (int)Evaluate(call.Arguments[0], frame).Number;

            if (!array.Within(index))
            {
                throw new InterpreterRuntimeException(
                    $"Subscript out of range: {call.Name}({index}).", call.Line);
            }

            return array[index];
        }

        if (Library.Has(call.Name))
        {
            var values = call.Arguments.Select(a => Evaluate(a, frame)).ToList();

            return Library.Call(call.Name, values, call.Line, _random);
        }

        if (_procedures.TryGetValue(call.Name, out var procedure))
            return RunToCompletion(procedure, call.Arguments, frame, call.Line);

        throw new InterpreterRuntimeException($"{call.Name} is not defined.", call.Line);
    }

    /// <summary>
    /// Runs a function called from inside an expression, to the end.
    ///
    /// An expression cannot be left half worked out, so this one call runs
    /// through rather than being stepped into. Stepping into a function is
    /// still possible where it is called as a statement.
    /// </summary>
    private BasicValue RunToCompletion(
        Procedure procedure, IReadOnlyList<Expression> arguments, Frame caller, int line)
    {
        var depth = _frames.Count;

        Enter(procedure, arguments, caller, line);

        var guard = 0;

        while (_frames.Count > depth)
        {
            if (++guard > 5_000_000)
                throw new InterpreterRuntimeException("This function does not finish.", line);

            if (!ExecuteNextStatement(CancellationToken.None)) break;
        }

        return caller.LastCallResult ?? BasicValue.Default(procedure.ReturnType);
    }

    // Variables

    private BasicValue ReadVariable(Scope scope, string name)
    {
        if (scope.Variables.TryGetValue(name, out var value)) return value;

        // A procedure sees the top level's variables too, as QuickBASIC's
        // SHARED does; a name never assigned starts at zero.
        if (!ReferenceEquals(scope, _globals)
            && _globals.Variables.TryGetValue(name, out var shared))
        {
            return shared;
        }

        return BasicValue.Default(TypeSuffix.Of(name));
    }

    private void SetVariable(Scope scope, string name, BasicValue value)
    {
        var converted = Convert(value, TypeSuffix.Of(name));

        if (!scope.Variables.ContainsKey(name)
            && !ReferenceEquals(scope, _globals)
            && _globals.Variables.ContainsKey(name))
        {
            _globals.Variables[name] = converted;
            return;
        }

        scope.Variables[name] = converted;
    }

    private BasicArray? FindArray(Scope scope, string name)
    {
        if (scope.Arrays.TryGetValue(name, out var array)) return array;

        return _globals.Arrays.TryGetValue(name, out var shared) ? shared : null;
    }

    /// <summary>Puts a value into the type a variable holds.</summary>
    private static BasicValue Convert(BasicValue value, BasicType type)
    {
        if (type == BasicType.String)
            return value.IsString ? value : BasicValue.Of(value.Written());

        return value.IsString ? BasicValue.Of(0, type) : BasicValue.Of(value.Number, type);
    }


    // Flattening

    /// <summary>
    /// Turns a body of statements into a flat list of steps.
    ///
    /// Jumps are written as indices into the list, so running the program is
    /// walking an index rather than recursing — which is what lets it stop
    /// anywhere and carry on.
    /// </summary>
    private static IReadOnlyList<Step> Flatten(IReadOnlyList<Statement> body)
    {
        var steps = new List<Step>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gotos = new List<(int Index, string Label)>();

        FlattenInto(body, steps, labels, gotos, exits: []);

        // Labels are resolved afterwards, since a GOTO may point forwards.
        foreach (var (index, label) in gotos)
        {
            if (!labels.TryGetValue(label, out var target)) continue;

            // A GOSUB stays a GOSUB: turning it into a jump here is exactly
            // what would lose the address RETURN comes back to.
            steps[index] = steps[index] is Step.Gosub
                ? new Step.Gosub(target, steps[index].Line)
                : new Step.Jump(target, steps[index].Line);
        }

        return steps;
    }

    /// <summary>
    /// Writes one body of statements into the step list.
    ///
    /// <paramref name="exits"/> collects the steps an EXIT should jump from,
    /// which cannot be filled in until the end of the loop is known.
    /// </summary>
    private static void FlattenInto(
        IReadOnlyList<Statement> body,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos,
        List<int> exits)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case LabelStatement label:
                    labels[label.Name] = steps.Count;
                    steps.Add(new Step.Execute(statement, statement.Line));
                    break;

                case GotoStatement jump:
                    gotos.Add((steps.Count, jump.Label));
                    steps.Add(new Step.Jump(-1, jump.Line));
                    break;

                case GosubStatement call:
                    gotos.Add((steps.Count, call.Label));
                    steps.Add(new Step.Gosub(-1, call.Line));
                    break;

                case EndStatement:
                    steps.Add(new Step.End(statement.Line));
                    break;

                case IfStatement conditional:
                    FlattenIf(conditional, steps, labels, gotos, exits);
                    break;

                case ForStatement loop:
                    FlattenFor(loop, steps, labels, gotos);
                    break;

                case WhileStatement loop:
                    FlattenWhile(loop, steps, labels, gotos);
                    break;

                case DoStatement loop:
                    FlattenDo(loop, steps, labels, gotos);
                    break;

                case SelectStatement select:
                    FlattenSelect(select, steps, labels, gotos, exits);
                    break;

                case ExitStatement:
                    // Where it goes is known only once the loop ends.
                    exits.Add(steps.Count);
                    steps.Add(new Step.Jump(-1, statement.Line));
                    break;

                default:
                    steps.Add(new Step.Execute(statement, statement.Line));
                    break;
            }
        }
    }

    private static void FlattenIf(
        IfStatement statement,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos,
        List<int> exits)
    {
        // Each branch tests, runs, then jumps past the rest.
        var toEnd = new List<int>();

        var branches = new List<(Expression Condition, IReadOnlyList<Statement> Body)>
        {
            (statement.Condition, statement.Then)
        };

        branches.AddRange(statement.ElseIfs);

        foreach (var (condition, branchBody) in branches)
        {
            var test = steps.Count;
            steps.Add(new Step.JumpUnless(condition, -1, condition.Line));

            FlattenInto(branchBody, steps, labels, gotos, exits);

            toEnd.Add(steps.Count);
            steps.Add(new Step.Jump(-1, condition.Line));

            steps[test] = new Step.JumpUnless(condition, steps.Count, condition.Line);
        }

        if (statement.Else is { } otherwise)
            FlattenInto(otherwise, steps, labels, gotos, exits);

        foreach (var index in toEnd)
            steps[index] = new Step.Jump(steps.Count, steps[index].Line);
    }

    private static void FlattenFor(
        ForStatement loop,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos)
    {
        var init = steps.Count;
        steps.Add(new Step.ForInit(
            loop.Variable, loop.From, loop.To, loop.Step, -1, loop.Line));

        var bodyStart = steps.Count;
        var exits = new List<int>();

        FlattenInto(loop.Body, steps, labels, gotos, exits);

        steps.Add(new Step.ForNext(loop.Variable, bodyStart, loop.Line));

        var after = steps.Count;

        steps[init] = new Step.ForInit(
            loop.Variable, loop.From, loop.To, loop.Step, after, loop.Line);

        foreach (var index in exits)
            steps[index] = new Step.Jump(after, steps[index].Line);
    }

    private static void FlattenWhile(
        WhileStatement loop,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos)
    {
        var test = steps.Count;
        steps.Add(new Step.JumpUnless(loop.Condition, -1, loop.Line));

        var exits = new List<int>();

        FlattenInto(loop.Body, steps, labels, gotos, exits);

        steps.Add(new Step.Jump(test, loop.Line));

        var after = steps.Count;

        steps[test] = new Step.JumpUnless(loop.Condition, after, loop.Line);

        foreach (var index in exits)
            steps[index] = new Step.Jump(after, steps[index].Line);
    }

    private static void FlattenDo(
        DoStatement loop,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos)
    {
        var start = steps.Count;
        var exits = new List<int>();

        // A test at the top decides before the body runs; one at the bottom
        // lets the body run once whatever the answer.
        var test = -1;

        if (!loop.TestAtEnd && loop.Condition is { } condition)
        {
            test = steps.Count;
            steps.Add(new Step.JumpUnless(Negate(condition, loop.Until), -1, loop.Line));
        }

        FlattenInto(loop.Body, steps, labels, gotos, exits);

        if (loop.TestAtEnd && loop.Condition is { } endCondition)
        {
            steps.Add(new Step.JumpUnless(
                Negate(endCondition, loop.Until), steps.Count + 2, loop.Line));
        }

        steps.Add(new Step.Jump(start, loop.Line));

        var after = steps.Count;

        if (test >= 0)
        {
            steps[test] = new Step.JumpUnless(
                Negate(loop.Condition!, loop.Until), after, loop.Line);
        }

        foreach (var index in exits)
            steps[index] = new Step.Jump(after, steps[index].Line);
    }

    /// <summary>
    /// A DO UNTIL condition, turned into the WHILE it means.
    ///
    /// UNTIL x is WHILE NOT x, and the step list only knows how to carry on
    /// while something holds.
    /// </summary>
    private static Expression Negate(Expression condition, bool until) =>
        until
            ? new Binary("=", condition, new NumberLiteral(0, BasicType.Integer,
                condition.Line, condition.Column), condition.Line, condition.Column)
            : condition;

    private static void FlattenSelect(
        SelectStatement select,
        List<Step> steps,
        Dictionary<string, int> labels,
        List<(int Index, string Label)> gotos,
        List<int> exits)
    {
        var toEnd = new List<int>();

        foreach (var (values, caseBody) in select.Cases)
        {
            // CASE 1, 2, 3 holds when the value equals any of them.
            var condition = values
                .Select(v => (Expression)new Binary("=", select.Value, v, v.Line, v.Column))
                .Aggregate((a, b) => new Binary("OR", a, b, a.Line, a.Column));

            var test = steps.Count;
            steps.Add(new Step.JumpUnless(condition, -1, select.Line));

            FlattenInto(caseBody, steps, labels, gotos, exits);

            toEnd.Add(steps.Count);
            steps.Add(new Step.Jump(-1, select.Line));

            steps[test] = new Step.JumpUnless(condition, steps.Count, select.Line);
        }

        if (select.Otherwise is { } otherwise)
            FlattenInto(otherwise, steps, labels, gotos, exits);

        foreach (var index in toEnd)
            steps[index] = new Step.Jump(steps.Count, steps[index].Line);
    }

    // What the debugger asks for

    public override IReadOnlyList<InterpreterFrame> GetCallStack() =>
    [
        .. Enumerable.Reverse(_frames)
            .Select(f => new InterpreterFrame(f.Scope.Name, f.Scope.Line))
    ];

    public override IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex)
    {
        if (FrameAt(frameIndex) is not { } frame) return [];

        var variables = frame.Scope.Variables
            .Select(v => new InterpreterVariable(v.Key, v.Value.Display().Trim(), v.Value.TypeName()))
            .ToList();

        variables.AddRange(frame.Scope.Arrays.Select(a => new InterpreterVariable(
            $"{a.Key}()", $"({a.Value.UpperBound + 1} elements)", a.Value.Type.ToString(), true)));

        // A procedure can see the top level's variables, so they are shown
        // too rather than leaving the panel looking empty inside a call.
        if (!ReferenceEquals(frame.Scope, _globals))
        {
            variables.AddRange(_globals.Variables
                .Where(v => !frame.Scope.Variables.ContainsKey(v.Key))
                .Select(v => new InterpreterVariable(
                    v.Key, v.Value.Display().Trim(), v.Value.TypeName())));
        }

        return [.. variables.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Works out an expression written in BASIC, where the program is stopped.
    ///
    /// The same evaluator the program runs on, so a watch expression means
    /// exactly what it would mean in the program.
    /// </summary>
    public override string? Evaluate(string expression, int frameIndex)
    {
        if (FrameAt(frameIndex) is not { } frame) return null;

        try
        {
            // The parser reads programs, not loose expressions, so the
            // expression is given to it as one: an assignment whose value is
            // what we want worked out.
            var program = Parser.Parse($"__watch__ = {expression}");

            if (program.Diagnostics.Count > 0) return null;

            var parsed = program.Main
                .OfType<Assignment>()
                .FirstOrDefault()?.Value;

            return parsed is null ? null : Evaluate(parsed, frame).Written();
        }
        catch (InterpreterRuntimeException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public override bool SetVariable(string name, string value, int frameIndex)
    {
        if (FrameAt(frameIndex) is not { } frame) return false;

        var type = TypeSuffix.Of(name);

        var parsed = type == BasicType.String
            ? BasicValue.Of(value)
            : double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? BasicValue.Of(number, type)
                : default;

        if (!parsed.IsString && parsed.Number == 0 && type != BasicType.String
            && !double.TryParse(value, out _))
        {
            return false;
        }

        SetVariable(frame.Scope, name, parsed);

        return true;
    }

    /// <summary>The frame at an index counted from the innermost.</summary>
    private Frame? FrameAt(int index)
    {
        var position = _frames.Count - 1 - index;

        return position >= 0 && position < _frames.Count ? _frames[position] : null;
    }

    /// <summary>One call in progress.</summary>
    private sealed class Frame(Scope scope, IReadOnlyList<Step> steps, Procedure? procedure)
    {
        public Scope Scope { get; } = scope;
        public IReadOnlyList<Step> Steps { get; } = steps;
        public Procedure? Procedure { get; } = procedure;

        /// <summary>Which step comes next.</summary>
        public int Index { get; set; }

        /// <summary>What a FUNCTION is giving back.</summary>
        public BasicValue? ReturnValue { get; set; }

        /// <summary>What the last call from here gave back.</summary>
        public BasicValue? LastCallResult { get; set; }

        /// <summary>The FOR loops running here, by their counter.</summary>
        public Dictionary<string, (double To, double Step)> Loops { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int CurrentLine =>
            Index < Steps.Count ? Steps[Index].Line : Scope.Line;
    }
}
