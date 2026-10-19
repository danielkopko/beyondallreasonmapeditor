using System.Globalization;
using System.Text;

namespace MapCore.Lua;

public enum LuaTokenKind { Name, Number, String, Symbol }

/// <summary>A token with its position in the source. <see cref="Value"/> is the double or decoded string of a literal.</summary>
public readonly record struct LuaToken(LuaTokenKind Kind, int Start, int Length, string Text, object? Value = null)
{
    public int End => Start + Length;
    public bool Is(string symbolOrName) => (Kind is LuaTokenKind.Symbol or LuaTokenKind.Name) && Text == symbolOrName;
}

public sealed class LuaSyntaxException(string message, int line) : FormatException($"Lua syntax error on line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>Splits Lua 5.1 source into tokens, skipping whitespace and comments.</summary>
public static class LuaLexer
{
    static readonly string[] Symbols =
        ["...", "..", "==", "~=", "<=", ">=", "//", "::", "<<", ">>",
         "+", "-", "*", "/", "%", "^", "#", "&", "~", "|", "<", ">", "=", "(", ")", "{", "}", "[", "]", ";", ":", ",", "."];

    public static List<LuaToken> Tokenize(string source)
    {
        var tokens = new List<LuaToken>();
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && At(source, i + 1) == '-')
            {
                i = SkipComment(source, i);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    i++;
                tokens.Add(new(LuaTokenKind.Name, start, i - start, source[start..i]));
            }
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(At(source, i + 1))))
            {
                tokens.Add(ReadNumber(source, ref i));
            }
            else if (c is '"' or '\'')
            {
                tokens.Add(ReadQuotedString(source, ref i));
            }
            else if (c == '[' && LongBracketLevel(source, i) >= 0)
            {
                int start = i;
                var text = ReadLongBracket(source, ref i);
                tokens.Add(new(LuaTokenKind.String, start, i - start, source[start..i], text));
            }
            else
            {
                var symbol = Symbols.FirstOrDefault(s => string.CompareOrdinal(source, i, s, 0, s.Length) == 0)
                    ?? throw Error(source, i, $"unexpected character '{c}'");
                tokens.Add(new(LuaTokenKind.Symbol, i, symbol.Length, symbol));
                i += symbol.Length;
            }
        }
        return tokens;
    }

    /// <summary>Line number (1-based) of a character position, for messages.</summary>
    public static int LineOf(string source, int position) =>
        1 + source.AsSpan(0, Math.Min(position, source.Length)).Count('\n');

    static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    static int SkipComment(string source, int i)
    {
        i += 2;
        if (At(source, i) == '[' && LongBracketLevel(source, i) >= 0)
        {
            ReadLongBracket(source, ref i);
            return i;
        }
        while (i < source.Length && source[i] != '\n')
            i++;
        return i;
    }

    /// <summary>The number of '=' in an opening long bracket ([[, [=[, ...) at <paramref name="i"/>, or -1.</summary>
    static int LongBracketLevel(string source, int i)
    {
        int j = i + 1, level = 0;
        while (At(source, j) == '=')
        {
            j++;
            level++;
        }
        return At(source, j) == '[' ? level : -1;
    }

    static string ReadLongBracket(string source, ref int i)
    {
        int start = i, level = LongBracketLevel(source, i);
        i += level + 2;
        if (At(source, i) == '\r')
            i++;
        if (At(source, i) == '\n')
            i++; // a newline straight after the opening bracket is not part of the text
        var close = "]" + new string('=', level) + "]";
        int end = source.IndexOf(close, i, StringComparison.Ordinal);
        if (end < 0)
            throw Error(source, start, "unfinished long string or comment");
        var text = source[i..end];
        i = end + close.Length;
        return text;
    }

    static LuaToken ReadNumber(string source, ref int i)
    {
        int start = i;
        double value;
        if (source[i] == '0' && (At(source, i + 1) is 'x' or 'X'))
        {
            i += 2;
            double mantissa = 0;
            int exponent = 0;
            bool fraction = false;
            while (Uri.IsHexDigit(At(source, i)) || (At(source, i) == '.' && !fraction))
            {
                if (source[i] == '.')
                    fraction = true;
                else
                {
                    mantissa = mantissa * 16 + Convert.ToInt32(source[i].ToString(), 16);
                    if (fraction)
                        exponent -= 4;
                }
                i++;
            }
            if (At(source, i) is 'p' or 'P')
            {
                int expStart = ++i;
                if (At(source, i) is '+' or '-')
                    i++;
                while (char.IsDigit(At(source, i)))
                    i++;
                exponent += int.Parse(source[expStart..i], CultureInfo.InvariantCulture);
            }
            value = mantissa * Math.Pow(2, exponent);
        }
        else
        {
            while (char.IsDigit(At(source, i)) || At(source, i) == '.')
                i++;
            if (At(source, i) is 'e' or 'E')
            {
                i++;
                if (At(source, i) is '+' or '-')
                    i++;
                while (char.IsDigit(At(source, i)))
                    i++;
            }
            if (!double.TryParse(source.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw Error(source, start, $"malformed number '{source[start..i]}'");
        }
        return new(LuaTokenKind.Number, start, i - start, source[start..i], value);
    }

    static LuaToken ReadQuotedString(string source, ref int i)
    {
        int start = i;
        char quote = source[i++];
        var text = new StringBuilder();
        while (true)
        {
            if (i >= source.Length || source[i] == '\n')
                throw Error(source, start, "unfinished string");
            char c = source[i++];
            if (c == quote)
                break;
            if (c != '\\')
            {
                text.Append(c);
                continue;
            }
            char e = At(source, i++);
            switch (e)
            {
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'v': text.Append('\v'); break;
                case '\n': text.Append('\n'); break;
                case 'x':
                    text.Append((char)Convert.ToInt32(source.Substring(i, 2), 16));
                    i += 2;
                    break;
                case 'z':
                    while (i < source.Length && char.IsWhiteSpace(source[i]))
                        i++;
                    break;
                default:
                    if (char.IsDigit(e))
                    {
                        int digitsStart = i - 1;
                        while (i - digitsStart < 3 && char.IsDigit(At(source, i)))
                            i++;
                        text.Append((char)int.Parse(source[digitsStart..i], CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(e); // \\, \", \' and anything else stand for themselves
                    }
                    break;
            }
        }
        return new(LuaTokenKind.String, start, i - start, source[start..i], text.ToString());
    }

    static LuaSyntaxException Error(string source, int position, string message) => new(message, LineOf(source, position));
}
