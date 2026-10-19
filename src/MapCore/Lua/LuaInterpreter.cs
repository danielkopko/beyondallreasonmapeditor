using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using static MapCore.Lua.LuaValuesRuntime;

namespace MapCore.Lua;

/// <summary>
/// Runs Lua 5.1 scripts in a sandbox, for the definition files maps carry (features/*.lua build their features with
/// loops and concatenation, so reading their text isn't enough). The whole language is there, but only a few safe
/// library functions: pairs, ipairs, next, type, tostring, tonumber, select, unpack, error, assert, pcall, rawget,
/// rawset, setmetatable/getmetatable (__index only), table.insert/remove/concat, math.floor/ceil/min/max/abs/sqrt/fmod
/// /huge/pi, string.lower/upper/len/sub/rep, and the engine's lowerkeys. Nothing touches files, the OS or the engine;
/// calling anything else is an error. Every run is bounded by <see cref="LuaLimits"/> and runs on a thread of its own
/// with a large stack, so a hostile or broken file can neither hang nor crash the editor.
/// </summary>
public sealed class LuaInterpreter
{
    const int MaxNesting = 1500, StackSize = 16 * 1024 * 1024;
    readonly LuaBudget budget;
    readonly LuaMap globals;
    readonly LuaMap stringLibrary;
    int callDepth, nesting, line;

    public LuaInterpreter(LuaLimits? limits = null)
    {
        budget = new LuaBudget(limits ?? LuaLimits.Default);
        globals = new LuaMap(budget);
        stringLibrary = new LuaMap(budget);
        SetUpLibrary();
    }

    public LuaMap Globals => globals;

    /// <summary>
    /// Runs a chunk and returns what it returns. A syntax error throws <see cref="LuaSyntaxException"/>; anything
    /// going wrong while it runs throws <see cref="LuaRuntimeException"/> naming the line.
    /// </summary>
    public object?[] Run(string source, string chunkName = "chunk")
    {
        var block = LuaParser.Parse(source);
        return OnOwnThread(() => Call(new Closure(new FunctionExpr(1, chunkName, [], true, block), null), []), chunkName);
    }

    /// <summary>
    /// A function the host provides, for scripts to call: a harness standing in for the engine, say. It gets the
    /// arguments and returns the results; throwing <see cref="LuaRuntimeException"/> is a Lua error.
    /// </summary>
    public static LuaFunction Function(string name, Func<object?[], object?[]> body) => new Builtin(name, body);

    /// <summary>Calls a Lua function (a script's own, or a host one) with the same limits as <see cref="Run"/>.</summary>
    public object?[] Invoke(object? function, params object?[] args) =>
        OnOwnThread(() => Call(function, args), function is LuaFunction f ? f.Name : "call");

    /// <summary>Runs on a thread of its own with a large stack, turning failures into Lua errors that name the line.</summary>
    object?[] OnOwnThread(Func<object?[]> body, string name)
    {
        object?[]? result = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (LuaLimitException ex)
            {
                failure = ExceptionDispatchInfo.Capture(new LuaLimitException($"line {line}: {ex.Message}"));
            }
            catch (LuaRuntimeException ex)
            {
                failure = ExceptionDispatchInfo.Capture(new LuaRuntimeException($"line {line}: {ex.Message}"));
            }
            catch (Exception ex) when (ex is InvalidCastException or ArgumentException or OverflowException or FormatException or IndexOutOfRangeException)
            {
                failure = ExceptionDispatchInfo.Capture(new LuaRuntimeException($"line {line}: {ex.Message}"));
            }
        }, StackSize) { IsBackground = true, Name = "Lua " + name };
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }

    // ---------------------------------------------------------------- functions and scopes

    sealed class Box(object? value)
    {
        public object? Value = value;
    }

    sealed class Scope(Scope? parent)
    {
        Dictionary<string, Box>? names;

        public void Declare(string name, object? value) => (names ??= [])[name] = new Box(value);

        public Box? Find(string name)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.names != null && s.names.TryGetValue(name, out var box))
                    return box;
            return null;
        }

        Scope? Parent => parent;
    }

    /// <summary>A running function's extra arguments (...) and, once it returns, its results.</summary>
    sealed class Frame(object?[] varargs)
    {
        public object?[] Varargs { get; } = varargs;
        public object?[] Returned { get; set; } = [];
    }

    sealed class Closure(FunctionExpr definition, Scope? scope) : LuaFunction
    {
        public FunctionExpr Definition { get; } = definition;
        public Scope? Scope { get; } = scope;
        public override string Name => Definition.Name;
    }

    sealed class Builtin(string name, Func<object?[], object?[]> body) : LuaFunction
    {
        public Func<object?[], object?[]> Body { get; } = body;
        public override string Name => name;
    }

    /// <summary>A value thrown by the script's own error(): pcall hands it back unchanged.</summary>
    sealed class ScriptError(object? value) : LuaRuntimeException(ToDisplayString(value))
    {
        public object? Value { get; } = value;
    }

    enum Signal { Normal, Break, Return }

    object?[] Call(object? function, object?[] args, string? what = null)
    {
        switch (function)
        {
            case Builtin b:
                budget.Step();
                return b.Body(args);
            case Closure c:
                if (++callDepth > budget.Limits.MaxCallDepth)
                {
                    callDepth--;
                    throw new LuaLimitException($"functions called each other too deeply (over {budget.Limits.MaxCallDepth})");
                }
                try
                {
                    var def = c.Definition;
                    var scope = new Scope(c.Scope);
                    for (int i = 0; i < def.Parameters.Count; i++)
                        scope.Declare(def.Parameters[i], i < args.Length ? args[i] : null);
                    var frame = new Frame(def.IsVararg && args.Length > def.Parameters.Count ? args[def.Parameters.Count..] : []);
                    return ExecBlock(def.Body, scope, frame) == Signal.Return ? frame.Returned : [];
                }
                finally
                {
                    callDepth--;
                }
            default:
                throw new LuaRuntimeException($"attempt to call a {TypeName(function)} value{(what == null ? "" : $" ({what})")}");
        }
    }

    // ---------------------------------------------------------------- statements

    Signal ExecBlock(LuaBlock block, Scope scope, Frame frame)
    {
        Enter();
        try
        {
            foreach (var statement in block.Statements)
            {
                var signal = Exec(statement, scope, frame);
                if (signal != Signal.Normal)
                    return signal;
            }
            return Signal.Normal;
        }
        finally
        {
            nesting--;
        }
    }

    void Enter()
    {
        if (++nesting > MaxNesting)
        {
            nesting--;
            throw new LuaLimitException("the script nested too deeply");
        }
    }

    Signal Exec(LuaStat statement, Scope scope, Frame frame)
    {
        budget.Step();
        line = statement.Line;
        switch (statement)
        {
            case LocalStat local:
            {
                var values = EvalList(local.Values, scope, frame, local.Names.Count);
                for (int i = 0; i < local.Names.Count; i++)
                    scope.Declare(local.Names[i], values[i]);
                return Signal.Normal;
            }
            case AssignStat assign:
            {
                // Lua works out where each target is before assigning anything (i, a[i] = i + 1, 20 sets the old a[i]).
                var places = assign.Targets.Select(t => t is IndexExpr ix
                    ? (Table: Eval(ix.Target, scope, frame), Key: Eval(ix.Key, scope, frame), Target: t)
                    : (Table: null, Key: null, Target: t)).ToList();
                var values = EvalList(assign.Values, scope, frame, places.Count);
                for (int i = 0; i < places.Count; i++)
                {
                    var (table, key, target) = places[i];
                    if (target is NameExpr name)
                        SetName(name.Name, values[i], scope);
                    else
                        SetIndex(table, key, values[i], ((IndexExpr)target).Target);
                }
                return Signal.Normal;
            }
            case CallStat call:
                EvalCall(call.Call, scope, frame);
                return Signal.Normal;
            case DoStat block:
                return ExecBlock(block.Body, new Scope(scope), frame);
            case WhileStat loop:
                while (IsTruthy(Eval(loop.Condition, scope, frame)))
                {
                    var signal = ExecBlock(loop.Body, new Scope(scope), frame);
                    if (signal == Signal.Break)
                        break;
                    if (signal == Signal.Return)
                        return signal;
                    budget.Step();
                }
                return Signal.Normal;
            case RepeatStat loop:
                while (true)
                {
                    var inner = new Scope(scope); // until sees the body's locals
                    var signal = ExecBlock(loop.Body, inner, frame);
                    if (signal == Signal.Break)
                        break;
                    if (signal == Signal.Return)
                        return signal;
                    if (IsTruthy(Eval(loop.Condition, inner, frame)))
                        break;
                    budget.Step();
                }
                return Signal.Normal;
            case IfStat choice:
                foreach (var (condition, body) in choice.Clauses)
                    if (IsTruthy(Eval(condition, scope, frame)))
                        return ExecBlock(body, new Scope(scope), frame);
                return choice.Otherwise == null ? Signal.Normal : ExecBlock(choice.Otherwise, new Scope(scope), frame);
            case NumericForStat loop:
            {
                double start = ForNumber(Eval(loop.Start, scope, frame), "initial value");
                double limit = ForNumber(Eval(loop.Limit, scope, frame), "limit");
                double step = loop.StepBy == null ? 1 : ForNumber(Eval(loop.StepBy, scope, frame), "step");
                for (double v = start; step > 0 ? v <= limit : v >= limit; v += step)
                {
                    budget.Step();
                    var inner = new Scope(scope);
                    inner.Declare(loop.Variable, v);
                    var signal = ExecBlock(loop.Body, inner, frame);
                    if (signal == Signal.Break)
                        break;
                    if (signal == Signal.Return)
                        return signal;
                }
                return Signal.Normal;
            }
            case GenericForStat loop:
            {
                var start = EvalList(loop.Values, scope, frame, 3);
                var (iterator, state, control) = (start[0], start[1], start[2]);
                while (true)
                {
                    budget.Step();
                    var results = Call(iterator, [state, control], "the 'for' iterator");
                    var first = results.Length > 0 ? results[0] : null;
                    if (first == null)
                        break;
                    control = first;
                    var inner = new Scope(scope);
                    for (int i = 0; i < loop.Names.Count; i++)
                        inner.Declare(loop.Names[i], i < results.Length ? results[i] : null);
                    var signal = ExecBlock(loop.Body, inner, frame);
                    if (signal == Signal.Break)
                        break;
                    if (signal == Signal.Return)
                        return signal;
                }
                return Signal.Normal;
            }
            case FunctionStat function:
            {
                var closure = new Closure(function.Function, scope);
                if (function.Target is NameExpr name)
                    SetName(name.Name, closure, scope);
                else
                {
                    var ix = (IndexExpr)function.Target;
                    SetIndex(Eval(ix.Target, scope, frame), Eval(ix.Key, scope, frame), closure, ix.Target);
                }
                return Signal.Normal;
            }
            case LocalFunctionStat function:
                scope.Declare(function.Name, null);
                scope.Find(function.Name)!.Value = new Closure(function.Function, scope); // it can call itself
                return Signal.Normal;
            case ReturnStat ret:
                frame.Returned = EvalList(ret.Values, scope, frame, -1);
                return Signal.Return;
            case BreakStat:
                return Signal.Break;
            default:
                throw new LuaRuntimeException("unknown statement");
        }
    }

    static double ForNumber(object? value, string what) =>
        ToNumber(value) ?? throw new LuaRuntimeException($"'for' {what} must be a number");

    void SetName(string name, object? value, Scope scope)
    {
        if (scope.Find(name) is Box box)
            box.Value = value;
        else
            globals.Set(name, value);
    }

    void SetIndex(object? table, object? key, object? value, LuaExpr described)
    {
        if (table is not LuaMap map)
            throw new LuaRuntimeException($"attempt to index a {TypeName(table)} value{Describe(described)}");
        map.Set(key, value);
    }

    // ---------------------------------------------------------------- expressions

    /// <summary>Values of an expression list, the last one expanding to all its values (a call, or ...); padded or cut to <paramref name="wanted"/> (-1: all).</summary>
    object?[] EvalList(List<LuaExpr> expressions, Scope scope, Frame frame, int wanted)
    {
        var values = new List<object?>(Math.Max(wanted, expressions.Count));
        for (int i = 0; i < expressions.Count; i++)
        {
            var e = expressions[i];
            if (i == expressions.Count - 1 && e is CallExpr or VarargExpr)
                values.AddRange(e is CallExpr call ? EvalCall(call, scope, frame) : frame.Varargs);
            else
                values.Add(Eval(e, scope, frame));
        }
        if (wanted >= 0)
        {
            while (values.Count < wanted)
                values.Add(null);
            if (values.Count > wanted)
                values.RemoveRange(wanted, values.Count - wanted);
        }
        return [.. values];
    }

    object? Eval(LuaExpr expression, Scope scope, Frame frame)
    {
        Enter();
        try
        {
            switch (expression)
            {
                case ConstantExpr c:
                    return c.Value;
                case NameExpr name:
                    return scope.Find(name.Name) is Box box ? box.Value : globals.Get(name.Name);
                case IndexExpr ix:
                    return Index(Eval(ix.Target, scope, frame), Eval(ix.Key, scope, frame), ix.Target);
                case CallExpr call:
                    var results = EvalCall(call, scope, frame);
                    return results.Length > 0 ? results[0] : null;
                case VarargExpr:
                    return frame.Varargs.Length > 0 ? frame.Varargs[0] : null;
                case ParenExpr paren:
                    return Eval(paren.Inner, scope, frame);
                case FunctionExpr function:
                    return new Closure(function, scope);
                case TableExpr table:
                    return BuildTable(table, scope, frame);
                case UnaryExpr unary:
                    return Unary(unary.Op, Eval(unary.Operand, scope, frame));
                case BinaryExpr binary:
                    if (binary.Op == "and")
                    {
                        var left = Eval(binary.Left, scope, frame);
                        return IsTruthy(left) ? Eval(binary.Right, scope, frame) : left;
                    }
                    if (binary.Op == "or")
                    {
                        var left = Eval(binary.Left, scope, frame);
                        return IsTruthy(left) ? left : Eval(binary.Right, scope, frame);
                    }
                    return Binary(binary.Op, Eval(binary.Left, scope, frame), Eval(binary.Right, scope, frame));
                default:
                    throw new LuaRuntimeException("unknown expression");
            }
        }
        finally
        {
            nesting--;
        }
    }

    object?[] EvalCall(CallExpr call, Scope scope, Frame frame)
    {
        if (call.Method != null)
        {
            var self = Eval(call.Function, scope, frame);
            var method = Index(self, call.Method, call.Function);
            var args = EvalList(call.Arguments, scope, frame, -1);
            return Call(method, [self, .. args], $"method '{call.Method}'");
        }
        var function = Eval(call.Function, scope, frame);
        return Call(function, EvalList(call.Arguments, scope, frame, -1), Describe(call.Function).Trim(' ', '(', ')'));
    }

    LuaMap BuildTable(TableExpr table, Scope scope, Frame frame)
    {
        var map = new LuaMap(budget);
        int position = 1;
        for (int i = 0; i < table.Fields.Count; i++)
        {
            var (key, value) = table.Fields[i];
            if (key != null)
            {
                var k = Eval(key, scope, frame);
                var v = Eval(value, scope, frame);
                if (k == null)
                    throw new LuaRuntimeException("table index is nil");
                map.Set(k, v);
            }
            else if (i == table.Fields.Count - 1 && value is CallExpr or VarargExpr)
            {
                foreach (var v in EvalList([value], scope, frame, -1))
                    map.Set((double)position++, v);
            }
            else
                map.Set((double)position++, Eval(value, scope, frame));
        }
        return map;
    }

    object? Index(object? target, object? key, LuaExpr? described = null)
    {
        for (int hops = 0; hops < 100; hops++)
        {
            switch (target)
            {
                case LuaMap map:
                    var value = map.Get(key);
                    if (value != null || map.Metatable?.Get("__index") is not object handler)
                        return value;
                    if (handler is LuaFunction f)
                    {
                        var results = Call(f, [map, key], "__index");
                        return results.Length > 0 ? results[0] : null;
                    }
                    target = handler;
                    continue;
                case string:
                    return stringLibrary.Get(key); // ("text"):lower()
                default:
                    throw new LuaRuntimeException($"attempt to index a {TypeName(target)} value{(described == null ? "" : Describe(described))}");
            }
        }
        throw new LuaLimitException("__index chains too long");
    }

    static string Describe(LuaExpr e) => e switch
    {
        NameExpr n => $" ('{n.Name}')",
        IndexExpr { Key: ConstantExpr { Value: string s } } => $" (field '{s}')",
        _ => "",
    };

    object? Unary(string op, object? value)
    {
        switch (op)
        {
            case "not":
                return !IsTruthy(value);
            case "-":
                return -(ToNumber(value) ?? throw Arithmetic(value));
            case "#":
                return value switch
                {
                    string s => (double)s.Length,
                    LuaMap t => (double)t.Length,
                    _ => throw new LuaRuntimeException($"attempt to get length of a {TypeName(value)} value"),
                };
            default:
                throw new LuaRuntimeException($"unknown operator {op}");
        }
    }

    static LuaRuntimeException Arithmetic(object? value) => new($"attempt to perform arithmetic on a {TypeName(value)} value");

    object? Binary(string op, object? a, object? b)
    {
        switch (op)
        {
            case "==":
                return RawEquals(a, b);
            case "~=":
                return !RawEquals(a, b);
            case "<":
                return Compare(a, b) < 0;
            case "<=":
                return Compare(a, b) <= 0;
            case ">":
                return Compare(a, b) > 0;
            case ">=":
                return Compare(a, b) >= 0;
            case "..":
                if (a is not (string or double))
                    throw new LuaRuntimeException($"attempt to concatenate a {TypeName(a)} value");
                if (b is not (string or double))
                    throw new LuaRuntimeException($"attempt to concatenate a {TypeName(b)} value");
                return budget.CheckLength(ToDisplayString(a) + ToDisplayString(b));
        }
        double x = ToNumber(a) ?? throw Arithmetic(a), y = ToNumber(b) ?? throw Arithmetic(b);
        return op switch
        {
            "+" => x + y,
            "-" => x - y,
            "*" => x * y,
            "/" => x / y,
            "%" => x - Math.Floor(x / y) * y,
            "^" => Math.Pow(x, y),
            _ => throw new LuaRuntimeException($"unknown operator {op}"),
        };
    }

    static bool RawEquals(object? a, object? b) => a switch
    {
        null => b == null,
        double x => b is double y && x == y,
        string s => b is string t && string.Equals(s, t, StringComparison.Ordinal),
        bool p => b is bool q && p == q,
        _ => ReferenceEquals(a, b),
    };

    static int Compare(object? a, object? b) => (a, b) switch
    {
        (double x, double y) => x < y ? -1 : x > y ? 1 : x == y ? 0 : throw new LuaRuntimeException("attempt to compare NaN"),
        (string s, string t) => string.CompareOrdinal(s, t),
        _ => throw new LuaRuntimeException($"attempt to compare {TypeName(a)} with {TypeName(b)}"),
    };

    // ---------------------------------------------------------------- the library

    void Register(LuaMap table, string name, Func<object?[], object?[]> body) => table.Set(name, new Builtin(name, body));

    static object? Arg(object?[] args, int i) => i < args.Length ? args[i] : null;

    static LuaMap TableArg(object?[] args, int i, string function) => Arg(args, i) as LuaMap ??
        throw new LuaRuntimeException($"bad argument #{i + 1} to '{function}' (table expected, got {TypeName(Arg(args, i))})");

    static double NumberArg(object?[] args, int i, string function) => ToNumber(Arg(args, i)) ??
        throw new LuaRuntimeException($"bad argument #{i + 1} to '{function}' (number expected, got {TypeName(Arg(args, i))})");

    static string StringArg(object?[] args, int i, string function) => Arg(args, i) switch
    {
        string s => s,
        double d => NumberToString(d),
        var v => throw new LuaRuntimeException($"bad argument #{i + 1} to '{function}' (string expected, got {TypeName(v)})"),
    };

    void SetUpLibrary()
    {
        var g = globals;
        g.Set("_G", g);
        g.Set("_VERSION", "Lua 5.1");
        Register(g, "pairs", args =>
        {
            var t = TableArg(args, 0, "pairs");
            var keys = t.SnapshotKeys();
            int i = 0;
            var iterator = new Builtin("pairs iterator", _ =>
            {
                while (i < keys.Length)
                {
                    var k = keys[i++];
                    if (t.Get(k) is object v)
                        return [k, v]; // entries cleared during the loop are skipped, as in Lua
                }
                return [null];
            });
            return [iterator, t, null];
        });
        Register(g, "ipairs", args =>
        {
            var t = TableArg(args, 0, "ipairs");
            var iterator = new Builtin("ipairs iterator", a =>
            {
                double i = (ToNumber(Arg(a, 1)) ?? 0) + 1;
                return t.Get(i) is object v ? [i, v] : [null];
            });
            return [iterator, t, 0.0];
        });
        Register(g, "next", args => TableArg(args, 0, "next").Next(Arg(args, 1)) is { } entry ? [entry.Key, entry.Value] : [null]);
        Register(g, "type", args => args.Length == 0
            ? throw new LuaRuntimeException("bad argument #1 to 'type' (value expected)") : [TypeName(args[0])]);
        Register(g, "tostring", args => [ToDisplayString(Arg(args, 0))]);
        Register(g, "tonumber", args =>
        {
            var v = Arg(args, 0);
            if (Arg(args, 1) is null)
                return [ToNumber(v)];
            int radix = (int)NumberArg(args, 1, "tonumber");
            if (radix is < 2 or > 36)
                throw new LuaRuntimeException("bad argument #2 to 'tonumber' (base out of range)");
            var text = StringArg(args, 0, "tonumber").Trim().ToLowerInvariant();
            if (text.Length == 0)
                return [null];
            double n = 0;
            foreach (char c in text)
            {
                int digit = char.IsDigit(c) ? c - '0' : c is >= 'a' and <= 'z' ? c - 'a' + 10 : 99;
                if (digit >= radix)
                    return [null];
                n = n * radix + digit;
            }
            return [n];
        });
        Register(g, "select", args =>
        {
            if (Arg(args, 0) is "#")
                return [(double)(args.Length - 1)];
            int n = (int)NumberArg(args, 0, "select");
            if (n < 0)
                n = args.Length + n;
            if (n < 1)
                throw new LuaRuntimeException("bad argument #1 to 'select' (index out of range)");
            return n < args.Length ? args[n..] : [];
        });
        Register(g, "unpack", args =>
        {
            var t = TableArg(args, 0, "unpack");
            int from = Arg(args, 1) is null ? 1 : (int)NumberArg(args, 1, "unpack");
            int to = Arg(args, 2) is null ? t.Length : (int)NumberArg(args, 2, "unpack");
            if (to - from >= 100_000)
                throw new LuaLimitException("unpack of too many values");
            var values = new List<object?>();
            for (int i = from; i <= to; i++)
                values.Add(t.Get((double)i));
            return [.. values];
        });
        Register(g, "error", args => throw new ScriptError(Arg(args, 0)));
        Register(g, "assert", args => IsTruthy(Arg(args, 0)) ? args
            : throw new ScriptError(Arg(args, 1) ?? "assertion failed!"));
        Register(g, "pcall", args =>
        {
            try
            {
                return [true, .. Call(Arg(args, 0), args.Length > 1 ? args[1..] : [])];
            }
            catch (LuaLimitException)
            {
                throw; // limits stop the whole run
            }
            catch (LuaRuntimeException ex)
            {
                return [false, ex is ScriptError se ? se.Value : ex.Message];
            }
        });
        Register(g, "rawget", args => [TableArg(args, 0, "rawget").Get(Arg(args, 1))]);
        Register(g, "rawset", args =>
        {
            TableArg(args, 0, "rawset").Set(Arg(args, 1), Arg(args, 2));
            return [args[0]];
        });
        Register(g, "setmetatable", args =>
        {
            var t = TableArg(args, 0, "setmetatable");
            var mt = Arg(args, 1) as LuaMap;
            if (Arg(args, 1) != null && mt == null)
                throw new LuaRuntimeException("bad argument #2 to 'setmetatable' (nil or table expected)");
            if (mt != null && mt.Entries.Any(e => e.Key is string s && s.StartsWith("__", StringComparison.Ordinal) && s != "__index"))
                throw new LuaRuntimeException("only __index is supported in metatables here");
            t.Metatable = mt;
            return [t];
        });
        Register(g, "getmetatable", args => [Arg(args, 0) is LuaMap t ? t.Metatable : null]);
        Register(g, "lowerkeys", args => [LowerKeys(Arg(args, 0), 0, [])]);

        var table = new LuaMap(budget);
        g.Set("table", table);
        Register(table, "insert", args =>
        {
            var t = TableArg(args, 0, "insert");
            int n = t.Length;
            if (args.Length == 2)
                t.Set((double)(n + 1), args[1]);
            else if (args.Length >= 3)
            {
                int pos = (int)NumberArg(args, 1, "insert");
                if (pos < 1 || pos > n + 1)
                    throw new LuaRuntimeException("bad argument #2 to 'insert' (position out of bounds)");
                for (int i = n; i >= pos; i--)
                {
                    budget.Step();
                    t.Set((double)(i + 1), t.Get((double)i));
                }
                t.Set((double)pos, args[2]);
            }
            else
                throw new LuaRuntimeException("wrong number of arguments to 'insert'");
            return [];
        });
        Register(table, "remove", args =>
        {
            var t = TableArg(args, 0, "remove");
            int n = t.Length;
            if (n == 0)
                return [null];
            int pos = Arg(args, 1) is null ? n : (int)NumberArg(args, 1, "remove");
            var removed = t.Get((double)pos);
            for (int i = pos; i < n; i++)
            {
                budget.Step();
                t.Set((double)i, t.Get((double)(i + 1)));
            }
            t.Set((double)n, null);
            return [removed];
        });
        Register(table, "concat", args =>
        {
            var t = TableArg(args, 0, "concat");
            var separator = Arg(args, 1) is null ? "" : StringArg(args, 1, "concat");
            int from = Arg(args, 2) is null ? 1 : (int)NumberArg(args, 2, "concat");
            int to = Arg(args, 3) is null ? t.Length : (int)NumberArg(args, 3, "concat");
            var text = new StringBuilder();
            for (int i = from; i <= to; i++)
            {
                budget.Step();
                var v = t.Get((double)i);
                if (v is not (string or double))
                    throw new LuaRuntimeException($"invalid value (at index {i}) in table for 'concat'");
                if (i > from)
                    text.Append(separator);
                text.Append(ToDisplayString(v));
                if (text.Length > budget.Limits.MaxStringLength)
                    throw new LuaLimitException($"the script built a string over {budget.Limits.MaxStringLength:N0} characters");
            }
            return [text.ToString()];
        });

        var math = new LuaMap(budget);
        g.Set("math", math);
        math.Set("huge", double.PositiveInfinity);
        math.Set("pi", Math.PI);
        Register(math, "floor", args => [Math.Floor(NumberArg(args, 0, "floor"))]);
        Register(math, "ceil", args => [Math.Ceiling(NumberArg(args, 0, "ceil"))]);
        Register(math, "abs", args => [Math.Abs(NumberArg(args, 0, "abs"))]);
        Register(math, "sqrt", args => [Math.Sqrt(NumberArg(args, 0, "sqrt"))]);
        Register(math, "fmod", args => [NumberArg(args, 0, "fmod") % NumberArg(args, 1, "fmod")]);
        Register(math, "sin", args => [Math.Sin(NumberArg(args, 0, "sin"))]);
        Register(math, "cos", args => [Math.Cos(NumberArg(args, 0, "cos"))]);
        Register(math, "atan", args => [Math.Atan(NumberArg(args, 0, "atan"))]);
        Register(math, "min", args => [Enumerable.Range(0, Math.Max(1, args.Length)).Min(i => NumberArg(args, i, "min"))]);
        Register(math, "max", args => [Enumerable.Range(0, Math.Max(1, args.Length)).Max(i => NumberArg(args, i, "max"))]);

        var s = stringLibrary;
        g.Set("string", s);
        Register(s, "lower", args => [StringArg(args, 0, "lower").ToLowerInvariant()]);
        Register(s, "upper", args => [StringArg(args, 0, "upper").ToUpperInvariant()]);
        Register(s, "len", args => [(double)StringArg(args, 0, "len").Length]);
        Register(s, "sub", args =>
        {
            var text = StringArg(args, 0, "sub");
            int n = text.Length;
            int from = Arg(args, 1) is null ? 1 : (int)NumberArg(args, 1, "sub");
            int to = Arg(args, 2) is null ? -1 : (int)NumberArg(args, 2, "sub");
            if (from < 0)
                from = Math.Max(n + from + 1, 1);
            else if (from == 0)
                from = 1;
            if (to < 0)
                to = n + to + 1;
            else if (to > n)
                to = n;
            return [from <= to ? text.Substring(from - 1, to - from + 1) : ""];
        });
        Register(s, "rep", args =>
        {
            var text = StringArg(args, 0, "rep");
            int count = (int)NumberArg(args, 1, "rep");
            if (count <= 0 || text.Length == 0)
                return [""];
            if ((long)text.Length * count > budget.Limits.MaxStringLength)
                throw new LuaLimitException($"the script built a string over {budget.Limits.MaxStringLength:N0} characters");
            return [string.Concat(Enumerable.Repeat(text, count))];
        });
        Register(s, "format", args =>
        {
            var text = Format(StringArg(args, 0, "format"), args);
            if (text.Length > budget.Limits.MaxStringLength)
                throw new LuaLimitException($"the script built a string over {budget.Limits.MaxStringLength:N0} characters");
            return [text];
        });
    }

    /// <summary>
    /// Lua 5.1's string.format: C's printf directives, with flags (- + space # 0), a width and a precision, for %d %i
    /// %u %c %x %X %o %e %E %f %F %g %G %s %q and %%. Numbers for the integer directives are truncated, as Lua 5.1
    /// does (BAR's feature files write <c>string.format("rocks30_%s_%02d", biome, i)</c>).
    /// </summary>
    static string Format(string format, object?[] args)
    {
        var text = new StringBuilder();
        int next = 1;
        object? Next(char conversion)
        {
            if (next >= args.Length)
                throw new LuaRuntimeException($"bad argument #{next + 1} to 'format' (no value)");
            return args[next++];
        }
        double Number(char conversion)
        {
            int index = next;
            var value = Next(conversion);
            return LuaValuesRuntime.ToNumber(value) ?? throw new LuaRuntimeException($"bad argument #{index + 1} to 'format' (number expected, got {TypeName(value)})");
        }
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                text.Append(format[i]);
                continue;
            }
            if (++i >= format.Length)
                throw new LuaRuntimeException("invalid option '%' to 'format'");
            if (format[i] == '%')
            {
                text.Append('%');
                continue;
            }
            int flagsStart = i;
            while (i < format.Length && "-+ #0".Contains(format[i]))
                i++;
            var flags = format[flagsStart..i];
            int width = 0, precision = -1;
            while (i < format.Length && char.IsAsciiDigit(format[i]))
                width = width * 10 + (format[i++] - '0');
            if (i < format.Length && format[i] == '.')
            {
                i++;
                precision = 0;
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                    precision = precision * 10 + (format[i++] - '0');
            }
            if (i >= format.Length)
                throw new LuaRuntimeException("invalid format string to 'format'");
            char conversion = format[i];
            bool left = flags.Contains('-'), zero = flags.Contains('0') && !left, alternate = flags.Contains('#');
            string sign = "", body;
            switch (conversion)
            {
                case 'd' or 'i':
                {
                    long n = (long)Math.Truncate(Number(conversion));
                    sign = n < 0 ? "-" : flags.Contains('+') ? "+" : flags.Contains(' ') ? " " : "";
                    body = n == long.MinValue ? "9223372036854775808" : Math.Abs(n).ToString(CultureInfo.InvariantCulture);
                    if (precision >= 0)
                    {
                        body = precision == 0 && n == 0 ? "" : body.PadLeft(precision, '0');
                        zero = false;
                    }
                    break;
                }
                case 'u' or 'x' or 'X' or 'o':
                {
                    ulong n = unchecked((ulong)(long)Math.Truncate(Number(conversion)));
                    body = conversion switch
                    {
                        'x' => n.ToString("x", CultureInfo.InvariantCulture),
                        'X' => n.ToString("X", CultureInfo.InvariantCulture),
                        'o' => Convert.ToString(unchecked((long)n), 8),
                        _ => n.ToString(CultureInfo.InvariantCulture),
                    };
                    if (precision >= 0)
                    {
                        body = precision == 0 && n == 0 ? "" : body.PadLeft(precision, '0');
                        zero = false;
                    }
                    if (alternate && n != 0)
                        body = conversion switch { 'x' => "0x" + body, 'X' => "0X" + body, 'o' when !body.StartsWith('0') => "0" + body, _ => body };
                    break;
                }
                case 'c':
                    body = ((char)(int)Math.Truncate(Number(conversion))).ToString();
                    zero = false;
                    break;
                case 'e' or 'E' or 'f' or 'F' or 'g' or 'G':
                {
                    double n = Number(conversion);
                    sign = n < 0 || (n == 0 && double.IsNegative(n)) ? "-" : flags.Contains('+') ? "+" : flags.Contains(' ') ? " " : "";
                    body = FormatReal(Math.Abs(n), conversion, precision < 0 ? 6 : precision, alternate);
                    if (!double.IsFinite(n))
                        zero = false;
                    break;
                }
                case 's':
                {
                    body = ToDisplayString(Next(conversion));
                    if (precision >= 0 && body.Length > precision)
                        body = body[..precision];
                    zero = false;
                    break;
                }
                case 'q':
                {
                    var value = Next(conversion) is string q ? q : ToDisplayString(args[next - 1]);
                    body = "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\\n").Replace("\r", "\\r").Replace("\0", "\\000") + "\"";
                    zero = false;
                    break;
                }
                default:
                    throw new LuaRuntimeException($"invalid option '%{conversion}' to 'format'");
            }
            int pad = width - sign.Length - body.Length;
            if (pad <= 0)
                text.Append(sign).Append(body);
            else if (left)
                text.Append(sign).Append(body).Append(' ', pad);
            else if (zero)
                text.Append(sign).Append('0', pad).Append(body);
            else
                text.Append(' ', pad).Append(sign).Append(body);
        }
        return text.ToString();
    }

    /// <summary>A number of 0 or more as C's %e, %f or %g writes it (exponents of at least two digits; %g without trailing zeros unless #).</summary>
    static string FormatReal(double n, char conversion, int precision, bool alternate)
    {
        bool upper = char.IsUpper(conversion);
        if (double.IsNaN(n))
            return upper ? "NAN" : "nan";
        if (double.IsInfinity(n))
            return upper ? "INF" : "inf";
        string Exponential(int digits)
        {
            var e = n.ToString((upper ? "E" : "e") + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); // 1.5e+003
            int at = e.IndexOfAny(['e', 'E']);
            int exponent = int.Parse(e[(at + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var mantissa = e[..at];
            if (alternate && digits == 0)
                mantissa += ".";
            return mantissa + e[at] + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        }
        string Fixed(int digits)
        {
            var f = n.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return alternate && digits == 0 ? f + "." : f;
        }
        switch (char.ToLowerInvariant(conversion))
        {
            case 'e':
                return Exponential(precision);
            case 'f':
                return Fixed(precision);
            default:
            {
                int p = precision == 0 ? 1 : precision;
                var probe = n.ToString("E" + (p - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                int x = int.Parse(probe[(probe.IndexOf('E') + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                var result = p > x && x >= -4 ? Fixed(p - 1 - x) : Exponential(p - 1);
                if (!alternate)
                {
                    int e = result.IndexOfAny(['e', 'E']);
                    var mantissa = e < 0 ? result : result[..e];
                    if (mantissa.Contains('.'))
                        mantissa = mantissa.TrimEnd('0').TrimEnd('.');
                    result = e < 0 ? mantissa : mantissa + result[e..];
                }
                return result;
            }
        }
    }

    /// <summary>The engine's lowerkeys (gamedata/system.lua): a copy of a table with its string keys lower-cased, all the way down.</summary>
    LuaMap LowerKeys(object? value, int depth, Dictionary<LuaMap, LuaMap> done)
    {
        if (value is not LuaMap t)
            return new LuaMap(budget);
        if (done.TryGetValue(t, out var copy))
            return copy;
        if (depth > 100)
            throw new LuaLimitException("lowerkeys: tables nested too deeply");
        var result = new LuaMap(budget);
        done[t] = result;
        foreach (var (k, v) in t.Entries.ToList())
        {
            budget.Step();
            var nested = v is LuaMap ? LowerKeys(v, depth + 1, done) : v;
            result.Set(k is string s ? s.ToLowerInvariant() : k, nested);
        }
        return result;
    }

}
