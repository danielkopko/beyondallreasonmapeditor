namespace MapCore.Lua;

// The syntax tree of a Lua 5.1 chunk, as LuaParser builds it and LuaInterpreter runs it.

abstract class LuaExpr(int line)
{
    public int Line { get; } = line;
}

sealed class ConstantExpr(int line, object? value) : LuaExpr(line)
{
    public object? Value { get; } = value;
}

sealed class VarargExpr(int line) : LuaExpr(line);

sealed class NameExpr(int line, string name) : LuaExpr(line)
{
    public string Name { get; } = name;
}

sealed class IndexExpr(int line, LuaExpr target, LuaExpr key) : LuaExpr(line)
{
    public LuaExpr Target { get; } = target;
    public LuaExpr Key { get; } = key;
}

sealed class CallExpr(int line, LuaExpr function, string? method, List<LuaExpr> arguments) : LuaExpr(line)
{
    public LuaExpr Function { get; } = function;

    /// <summary>For <c>obj:method(...)</c>: the method name; <see cref="Function"/> is then the object.</summary>
    public string? Method { get; } = method;

    public List<LuaExpr> Arguments { get; } = arguments;
}

sealed class FunctionExpr(int line, string name, List<string> parameters, bool isVararg, LuaBlock body) : LuaExpr(line)
{
    public string Name { get; } = name;
    public List<string> Parameters { get; } = parameters;
    public bool IsVararg { get; } = isVararg;
    public LuaBlock Body { get; } = body;
}

/// <param name="Key">Null for a positional field.</param>
sealed record TableField(LuaExpr? Key, LuaExpr Value);

sealed class TableExpr(int line, List<TableField> fields) : LuaExpr(line)
{
    public List<TableField> Fields { get; } = fields;
}

sealed class BinaryExpr(int line, string op, LuaExpr left, LuaExpr right) : LuaExpr(line)
{
    public string Op { get; } = op;
    public LuaExpr Left { get; } = left;
    public LuaExpr Right { get; } = right;
}

sealed class UnaryExpr(int line, string op, LuaExpr operand) : LuaExpr(line)
{
    public string Op { get; } = op;
    public LuaExpr Operand { get; } = operand;
}

/// <summary>An expression in parentheses: only its first value counts.</summary>
sealed class ParenExpr(int line, LuaExpr inner) : LuaExpr(line)
{
    public LuaExpr Inner { get; } = inner;
}

abstract class LuaStat(int line)
{
    public int Line { get; } = line;
}

sealed class LuaBlock(List<LuaStat> statements)
{
    public List<LuaStat> Statements { get; } = statements;
}

sealed class LocalStat(int line, List<string> names, List<LuaExpr> values) : LuaStat(line)
{
    public List<string> Names { get; } = names;
    public List<LuaExpr> Values { get; } = values;
}

sealed class AssignStat(int line, List<LuaExpr> targets, List<LuaExpr> values) : LuaStat(line)
{
    public List<LuaExpr> Targets { get; } = targets;
    public List<LuaExpr> Values { get; } = values;
}

sealed class CallStat(int line, CallExpr call) : LuaStat(line)
{
    public CallExpr Call { get; } = call;
}

sealed class DoStat(int line, LuaBlock body) : LuaStat(line)
{
    public LuaBlock Body { get; } = body;
}

sealed class WhileStat(int line, LuaExpr condition, LuaBlock body) : LuaStat(line)
{
    public LuaExpr Condition { get; } = condition;
    public LuaBlock Body { get; } = body;
}

sealed class RepeatStat(int line, LuaBlock body, LuaExpr condition) : LuaStat(line)
{
    public LuaBlock Body { get; } = body;
    public LuaExpr Condition { get; } = condition;
}

sealed class IfStat(int line, List<(LuaExpr Condition, LuaBlock Body)> clauses, LuaBlock? otherwise) : LuaStat(line)
{
    public List<(LuaExpr Condition, LuaBlock Body)> Clauses { get; } = clauses;
    public LuaBlock? Otherwise { get; } = otherwise;
}

sealed class NumericForStat(int line, string variable, LuaExpr start, LuaExpr limit, LuaExpr? step, LuaBlock body) : LuaStat(line)
{
    public string Variable { get; } = variable;
    public LuaExpr Start { get; } = start;
    public LuaExpr Limit { get; } = limit;
    public LuaExpr? StepBy { get; } = step;
    public LuaBlock Body { get; } = body;
}

sealed class GenericForStat(int line, List<string> names, List<LuaExpr> values, LuaBlock body) : LuaStat(line)
{
    public List<string> Names { get; } = names;
    public List<LuaExpr> Values { get; } = values;
    public LuaBlock Body { get; } = body;
}

/// <summary><c>function a.b.c(...)</c> or <c>function a:m(...)</c> (the target is then a.m and self comes first).</summary>
sealed class FunctionStat(int line, LuaExpr target, FunctionExpr function) : LuaStat(line)
{
    public LuaExpr Target { get; } = target;
    public FunctionExpr Function { get; } = function;
}

sealed class LocalFunctionStat(int line, string name, FunctionExpr function) : LuaStat(line)
{
    public string Name { get; } = name;
    public FunctionExpr Function { get; } = function;
}

sealed class ReturnStat(int line, List<LuaExpr> values) : LuaStat(line)
{
    public List<LuaExpr> Values { get; } = values;
}

sealed class BreakStat(int line) : LuaStat(line);

/// <summary>
/// Parses Lua 5.1 source (as <see cref="LuaLexer"/> splits it) into a syntax tree. Nesting is limited, so hostile
/// source can't exhaust the stack.
/// </summary>
sealed class LuaParser
{
    const int MaxNesting = 200;
    readonly string source;
    readonly List<LuaToken> tokens;
    readonly List<int> lineStarts = [0];
    int position, depth;

    LuaParser(string source)
    {
        this.source = source;
        tokens = LuaLexer.Tokenize(source);
        for (int i = 0; i < source.Length; i++)
            if (source[i] == '\n')
                lineStarts.Add(i + 1);
    }

    public static LuaBlock Parse(string source)
    {
        var parser = new LuaParser(source);
        var block = parser.Block();
        if (parser.position < parser.tokens.Count)
            throw parser.Error($"'{parser.Peek.Text}' was not expected here");
        return block;
    }

    LuaToken Peek => tokens[position];
    bool AtEnd => position >= tokens.Count;
    int Line
    {
        get
        {
            int at = AtEnd ? source.Length : Peek.Start;
            int index = lineStarts.BinarySearch(at);
            return (index >= 0 ? index : ~index - 1) + 1;
        }
    }

    bool Check(string text) => !AtEnd && Peek.Is(text) && Peek.Kind != LuaTokenKind.String;

    bool Accept(string text)
    {
        if (!Check(text))
            return false;
        position++;
        return true;
    }

    void Expect(string text, string? what = null)
    {
        if (!Accept(text))
            throw Error($"'{text}' expected{(what == null ? "" : " " + what)}{(AtEnd ? " at the end" : $" near '{Peek.Text}'")}");
    }

    string ExpectName()
    {
        if (AtEnd || Peek.Kind != LuaTokenKind.Name || IsKeyword(Peek.Text))
            throw Error($"a name expected{(AtEnd ? " at the end" : $" near '{Peek.Text}'")}");
        return tokens[position++].Text;
    }

    static readonly HashSet<string> Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
    ];

    static bool IsKeyword(string name) => Keywords.Contains(name);

    LuaSyntaxException Error(string message) => new(message, Line);

    void Enter()
    {
        if (++depth > MaxNesting)
            throw Error("the code is nested too deeply");
    }

    bool BlockEnds => AtEnd || Check("end") || Check("else") || Check("elseif") || Check("until");

    LuaBlock Block()
    {
        Enter();
        var statements = new List<LuaStat>();
        while (!BlockEnds)
        {
            if (Check("return"))
            {
                int line = Line;
                position++;
                var values = BlockEnds || Check(";") ? [] : ExpressionList();
                Accept(";");
                statements.Add(new ReturnStat(line, values));
                break; // return must be the last statement of a block
            }
            if (Check("break"))
            {
                statements.Add(new BreakStat(Line));
                position++;
                Accept(";");
                break;
            }
            statements.Add(Statement());
            Accept(";");
        }
        depth--;
        return new LuaBlock(statements);
    }

    LuaStat Statement()
    {
        int line = Line;
        if (Accept("do"))
        {
            var body = Block();
            Expect("end", "to close 'do'");
            return new DoStat(line, body);
        }
        if (Accept("while"))
        {
            var condition = Expression();
            Expect("do");
            var body = Block();
            Expect("end", "to close 'while'");
            return new WhileStat(line, condition, body);
        }
        if (Accept("repeat"))
        {
            var body = Block();
            Expect("until");
            return new RepeatStat(line, body, Expression());
        }
        if (Accept("if"))
        {
            var clauses = new List<(LuaExpr, LuaBlock)>();
            var condition = Expression();
            Expect("then");
            clauses.Add((condition, Block()));
            LuaBlock? otherwise = null;
            while (true)
            {
                if (Accept("elseif"))
                {
                    var c = Expression();
                    Expect("then");
                    clauses.Add((c, Block()));
                }
                else if (Accept("else"))
                {
                    otherwise = Block();
                    Expect("end", "to close 'if'");
                    break;
                }
                else
                {
                    Expect("end", "to close 'if'");
                    break;
                }
            }
            return new IfStat(line, clauses, otherwise);
        }
        if (Accept("for"))
        {
            var first = ExpectName();
            if (Accept("="))
            {
                var start = Expression();
                Expect(",");
                var limit = Expression();
                var step = Accept(",") ? Expression() : null;
                Expect("do");
                var body = Block();
                Expect("end", "to close 'for'");
                return new NumericForStat(line, first, start, limit, step, body);
            }
            var names = new List<string> { first };
            while (Accept(","))
                names.Add(ExpectName());
            Expect("in");
            var values = ExpressionList();
            Expect("do");
            var loop = Block();
            Expect("end", "to close 'for'");
            return new GenericForStat(line, names, values, loop);
        }
        if (Accept("function"))
        {
            var name = ExpectName();
            LuaExpr target = new NameExpr(line, name);
            var fullName = name;
            bool method = false;
            while (Check(".") || Check(":"))
            {
                method = Check(":");
                position++;
                var part = ExpectName();
                fullName += (method ? ":" : ".") + part;
                target = new IndexExpr(line, target, new ConstantExpr(line, part));
                if (method)
                    break;
            }
            return new FunctionStat(line, target, FunctionBody(line, fullName, method));
        }
        if (Accept("local"))
        {
            if (Accept("function"))
            {
                var name = ExpectName();
                return new LocalFunctionStat(line, name, FunctionBody(line, name, false));
            }
            var names = new List<string> { ExpectName() };
            while (Accept(","))
                names.Add(ExpectName());
            var values = Accept("=") ? ExpressionList() : [];
            return new LocalStat(line, names, values);
        }

        var expression = SuffixedExpression();
        if (Check("=") || Check(","))
        {
            var targets = new List<LuaExpr> { expression };
            while (Accept(","))
                targets.Add(SuffixedExpression());
            Expect("=");
            foreach (var t in targets)
                if (t is not (NameExpr or IndexExpr))
                    throw Error("cannot assign to this");
            return new AssignStat(line, targets, ExpressionList());
        }
        if (expression is CallExpr call)
            return new CallStat(line, call);
        throw Error("a statement was expected (an assignment or a function call)");
    }

    FunctionExpr FunctionBody(int line, string name, bool method)
    {
        Enter();
        var parameters = method ? new List<string> { "self" } : [];
        bool vararg = false;
        Expect("(");
        if (!Check(")"))
        {
            do
            {
                if (Accept("..."))
                {
                    vararg = true;
                    break;
                }
                parameters.Add(ExpectName());
            }
            while (Accept(","));
        }
        Expect(")");
        var body = Block();
        Expect("end", $"to close function {name}");
        depth--;
        return new FunctionExpr(line, name, parameters, vararg, body);
    }

    List<LuaExpr> ExpressionList()
    {
        var list = new List<LuaExpr> { Expression() };
        while (Accept(","))
            list.Add(Expression());
        return list;
    }

    // Binary operator priorities (left, right), as in Lua 5.1's lparser.c; unary operators bind at 8.
    static readonly Dictionary<string, (int Left, int Right)> Priority = new()
    {
        ["or"] = (1, 1), ["and"] = (2, 2),
        ["<"] = (3, 3), [">"] = (3, 3), ["<="] = (3, 3), [">="] = (3, 3), ["~="] = (3, 3), ["=="] = (3, 3),
        [".."] = (5, 4), ["+"] = (6, 6), ["-"] = (6, 6), ["*"] = (7, 7), ["/"] = (7, 7), ["%"] = (7, 7), ["^"] = (10, 9),
    };

    const int UnaryPriority = 8;

    LuaExpr Expression(int limit = 0)
    {
        Enter();
        int line = Line;
        LuaExpr left;
        if (!AtEnd && Peek.Kind != LuaTokenKind.String && (Peek.Is("not") || Peek.Is("-") || Peek.Is("#")))
        {
            var op = tokens[position++].Text;
            left = new UnaryExpr(line, op, Expression(UnaryPriority));
        }
        else
            left = SimpleExpression();
        while (!AtEnd && Peek.Kind != LuaTokenKind.String && Priority.TryGetValue(Peek.Text, out var p) && p.Left > limit)
        {
            var op = tokens[position++].Text;
            left = new BinaryExpr(line, op, left, Expression(p.Right));
        }
        depth--;
        return left;
    }

    LuaExpr SimpleExpression()
    {
        int line = Line;
        if (AtEnd)
            throw Error("an expression expected at the end");
        var t = Peek;
        switch (t.Kind)
        {
            case LuaTokenKind.Number:
                position++;
                return new ConstantExpr(line, (double)t.Value!);
            case LuaTokenKind.String:
                position++;
                return new ConstantExpr(line, (string)t.Value!);
        }
        if (Accept("nil"))
            return new ConstantExpr(line, null);
        if (Accept("true"))
            return new ConstantExpr(line, true);
        if (Accept("false"))
            return new ConstantExpr(line, false);
        if (Accept("..."))
            return new VarargExpr(line);
        if (Check("{"))
            return Table();
        if (Accept("function"))
            return FunctionBody(line, "anonymous", false);
        return SuffixedExpression();
    }

    LuaExpr PrimaryExpression()
    {
        int line = Line;
        if (Accept("("))
        {
            var inner = Expression();
            Expect(")");
            return new ParenExpr(line, inner);
        }
        return new NameExpr(line, ExpectName());
    }

    LuaExpr SuffixedExpression()
    {
        Enter();
        var e = PrimaryExpression();
        while (!AtEnd)
        {
            int line = Line;
            if (Peek.Kind == LuaTokenKind.String)
                e = new CallExpr(line, e, null, [SimpleExpression()]); // f "text"
            else if (Accept("."))
                e = new IndexExpr(line, e, new ConstantExpr(line, ExpectName()));
            else if (Accept("["))
            {
                var key = Expression();
                Expect("]");
                e = new IndexExpr(line, e, key);
            }
            else if (Accept(":"))
            {
                var method = ExpectName();
                e = new CallExpr(line, e, method, Arguments());
            }
            else if (Check("(") || Check("{"))
                e = new CallExpr(line, e, null, Arguments());
            else
                break;
        }
        depth--;
        return e;
    }

    List<LuaExpr> Arguments()
    {
        if (!AtEnd && Peek.Kind == LuaTokenKind.String)
            return [SimpleExpression()];
        if (Check("{"))
            return [Table()];
        Expect("(");
        if (Accept(")"))
            return [];
        var list = ExpressionList();
        Expect(")");
        return list;
    }

    TableExpr Table()
    {
        Enter();
        int line = Line;
        Expect("{");
        var fields = new List<TableField>();
        while (!Check("}"))
        {
            if (Accept("["))
            {
                var key = Expression();
                Expect("]");
                Expect("=");
                fields.Add(new TableField(key, Expression()));
            }
            else if (!AtEnd && Peek.Kind == LuaTokenKind.Name && !IsKeyword(Peek.Text) &&
                     position + 1 < tokens.Count && tokens[position + 1].Is("=") && tokens[position + 1].Kind == LuaTokenKind.Symbol)
            {
                var name = tokens[position].Text;
                position += 2;
                fields.Add(new TableField(new ConstantExpr(line, name), Expression()));
            }
            else
                fields.Add(new TableField(null, Expression()));
            if (!Accept(",") && !Accept(";"))
                break;
        }
        Expect("}", "to close the table");
        depth--;
        return new TableExpr(line, fields);
    }
}
