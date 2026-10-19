using System.Globalization;
using System.Text;

namespace MapCore.Lua;

/// <summary>
/// A Lua file that returns a table (mapinfo.lua, featureplacer sets, ...), read and edited in place. Only the
/// text of an edited value changes; comments, layout and everything else are kept. Keys match case-insensitively.
/// Paths use '.' between keys; positional entries count from 1 ("objectlist.3.x").
/// </summary>
public sealed class LuaDocument
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    readonly Encoding encoding;
    readonly bool byteOrderMark;
    string text = "";
    List<LuaToken> tokens = [];
    LuaTable root = null!;

    /// <summary>The current source text, including any edits.</summary>
    public string Text => text;

    /// <summary>The returned table.</summary>
    public LuaTable Root => root;

    /// <summary>Name of the variable holding the table ("mapinfo"), or null when the table is returned directly.</summary>
    public string? RootName { get; private set; }

    public IReadOnlyList<LuaToken> Tokens => tokens;

    LuaDocument(string text, Encoding encoding, bool byteOrderMark)
    {
        this.encoding = encoding;
        this.byteOrderMark = byteOrderMark;
        Reparse(text);
    }

    /// <exception cref="LuaSyntaxException">The text is not Lua this reader understands.</exception>
    /// <exception cref="InvalidDataException">No returned table was found.</exception>
    public static LuaDocument Parse(string text) => new(text, StrictUtf8, false);

    public static LuaDocument Load(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>Decodes as UTF-8 when valid (keeping a byte-order mark), otherwise as Latin-1, so saving gives back the same bytes.</summary>
    public static LuaDocument FromBytes(byte[] bytes)
    {
        bool bom = bytes.AsSpan().StartsWith(Utf8ByteOrderMark);
        var body = bom ? bytes.AsSpan(3) : bytes.AsSpan();
        try
        {
            return new LuaDocument(StrictUtf8.GetString(body), StrictUtf8, bom);
        }
        catch (DecoderFallbackException)
        {
            return new LuaDocument(Encoding.Latin1.GetString(body), Encoding.Latin1, bom);
        }
    }

    public byte[] ToBytes()
    {
        var body = encoding.GetBytes(text);
        return byteOrderMark ? [.. Utf8ByteOrderMark, .. body] : body;
    }

    public void Save(string path) => FileSystemUtil.WriteAllBytesAtomic(path, ToBytes());

    // ---------------------------------------------------------------- reading

    public LuaValue? Get(string path) => Field(path)?.Value;

    public LuaField? Field(string path)
    {
        var keys = path.Split('.');
        LuaTable? current = root;
        for (int i = 0; i < keys.Length - 1 && current != null; i++)
            current = current[keys[i]] as LuaTable;
        return current?.Field(keys[^1]);
    }

    // ---------------------------------------------------------------- editing

    public void SetNumber(string path, double value) => SetLiteral(path, FormatNumber(value));

    public void SetString(string path, string value)
    {
        char quote = Get(path) is LuaString { Quote: '\'' } ? '\'' : '"';
        SetLiteral(path, QuoteString(value, quote));
    }

    /// <summary>Sets a table of numbers (e.g. a colour). With the same count, each number is replaced in place.</summary>
    public void SetNumbers(string path, IReadOnlyList<double> values)
    {
        if (Get(path) is LuaTable t && t.Fields.Count == values.Count && t.Fields.All(f => f.Position != null && f.Value is LuaNumber))
        {
            Splice(t.Fields.Select((f, i) => (f.Value.Start, f.Value.Length, FormatNumber(values[i]))).ToList());
            return;
        }
        SetLiteral(path, "{" + string.Join(", ", values.Select(FormatNumber)) + "}");
    }

    /// <summary>
    /// Sets a value from text as a person would type it, keeping the existing value's type: text stays text
    /// (version "5.17" is not turned into a number), numbers must parse as numbers, and number lists such as
    /// colours take "a,b,c". A missing key takes its type from the input. Quotes around text are optional.
    /// </summary>
    /// <exception cref="InvalidOperationException">The input does not fit the existing value, or the value is an expression.</exception>
    public void SetFromText(string path, string input)
    {
        string unquoted = input.Length >= 2 && input[0] == input[^1] && input[0] is '"' or '\'' ? input[1..^1] : input;
        var parts = input.Split(',');
        var numbers = parts.Select(p => TryParseNumber(p, out double v) ? v : double.NaN).ToArray();
        bool allNumbers = numbers.All(double.IsFinite);

        switch (Get(path))
        {
            case LuaString:
                SetString(path, unquoted);
                break;
            case LuaNumber when parts.Length == 1 && allNumbers:
                SetNumber(path, numbers[0]);
                break;
            case LuaTable t when allNumbers && t.Fields.All(f => f.Position != null && f.Value is LuaNumber):
                SetNumbers(path, numbers);
                break;
            case null when allNumbers && unquoted == input:
                if (parts.Length == 1)
                    SetNumber(path, numbers[0]);
                else
                    SetNumbers(path, numbers);
                break;
            case null:
                SetString(path, unquoted);
                break;
            case var existing:
                throw new InvalidOperationException($"Cannot set '{path}' (currently {DisplayText(existing)}) to '{input}'.");
        }
    }

    /// <summary>Replaces a value with Lua source text, or inserts the key (and any missing tables) if absent.</summary>
    public void SetLiteral(string path, string literal)
    {
        if (Get(path) is LuaValue existing)
        {
            Splice([(existing.Start, existing.Length, literal)]);
            return;
        }

        var keys = path.Split('.');
        var current = root;
        int depth = 0;
        for (; depth < keys.Length - 1; depth++)
        {
            var field = current.Field(keys[depth]);
            if (field == null)
                break;
            current = field.Value as LuaTable
                ?? throw new InvalidOperationException($"Cannot set '{path}': '{string.Join('.', keys[..(depth + 1)])}' is not a table.");
        }
        string value = literal;
        for (int i = keys.Length - 1; i > depth; i--)
            value = $"{{ {KeyText(keys[i])} = {value} }}";
        InsertField(current, $"{KeyText(keys[depth])} = {value}");
    }

    /// <summary>Adds a positional entry (Lua source text) at the end of the table at <paramref name="tablePath"/>; returns its position.</summary>
    public int Append(string tablePath, string literal)
    {
        var table = Get(tablePath) as LuaTable ?? throw new InvalidOperationException($"'{tablePath}' is not a table.");
        int position = table.Fields.Count(f => f.Position != null) + 1;
        InsertField(table, literal);
        return position;
    }

    /// <summary>
    /// Removes a field, with its separator. When it sits on a line of its own, the whole line goes.
    /// Positional entries after it move up by one.
    /// </summary>
    public void Remove(string path)
    {
        var field = Field(path) ?? throw new InvalidOperationException($"'{path}' does not exist.");
        int start = field.Start, end = field.Value.End;
        int after = tokens.FindIndex(t => t.Start >= end);
        if (after >= 0 && (tokens[after].Is(",") || tokens[after].Is(";")))
            end = tokens[after].End;

        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int lineEnd = text.IndexOf('\n', end);
        bool ownLine = string.IsNullOrWhiteSpace(text[lineStart..start]) &&
                       string.IsNullOrWhiteSpace(text[end..(lineEnd < 0 ? text.Length : lineEnd)]);
        if (ownLine)
            (start, end) = (lineStart, lineEnd < 0 ? text.Length : lineEnd + 1);
        else
            while (end < text.Length && text[end] == ' ')
                end++;
        Splice([(start, end - start, "")]);
    }

    /// <summary>Adds field text after the table's last field, matching its line-per-field or inline style.</summary>
    void InsertField(LuaTable table, string fieldText)
    {
        if (table.Fields.Count == 0)
        {
            Splice([(table.Start, table.Length, $"{{ {fieldText} }}")]);
            return;
        }

        var last = table.Fields[^1];
        int after = tokens.FindIndex(t => t.Start >= last.Value.End);
        bool separated = after >= 0 && (tokens[after].Is(",") || tokens[after].Is(";"));
        int insertAt = separated ? tokens[after].End : last.Value.End;

        int lineStart = text.LastIndexOf('\n', Math.Max(0, last.Start - 1)) + 1;
        string insertion;
        if (string.IsNullOrWhiteSpace(text[lineStart..last.Start]))
        {
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            insertion = (separated ? "" : ",") + newline + text[lineStart..last.Start] + fieldText + ",";
        }
        else
        {
            insertion = separated ? $" {fieldText}," : $", {fieldText}";
        }
        Splice([(insertAt, 0, insertion)]);
    }

    /// <summary>Replaces the whole text (e.g. to undo edits) and re-reads the table.</summary>
    public void ReplaceText(string newText) => Splice([(0, text.Length, newText)]);

    /// <summary>Applies text replacements and re-reads the table; on failure the text is left as it was.</summary>
    void Splice(IReadOnlyList<(int Start, int Length, string Replacement)> edits)
    {
        var updated = new StringBuilder(text);
        foreach (var (start, length, replacement) in edits.OrderByDescending(e => e.Start))
            updated.Remove(start, length).Insert(start, replacement);
        var previous = text;
        try
        {
            Reparse(updated.ToString());
        }
        catch
        {
            Reparse(previous);
            throw;
        }
    }

    void Reparse(string source)
    {
        var newTokens = LuaLexer.Tokenize(source);
        int r = newTokens.FindLastIndex(t => t.Kind == LuaTokenKind.Name && t.Text == "return");
        if (r < 0 || r + 1 >= newTokens.Count)
            throw new InvalidDataException("The file does not return a table.");

        string? name = null;
        int start = -1;
        if (newTokens[r + 1].Is("{"))
        {
            start = r + 1;
        }
        else if (newTokens[r + 1].Kind == LuaTokenKind.Name)
        {
            // `return mapinfo`: the table is the first `[local] mapinfo = { ... }`.
            name = newTokens[r + 1].Text;
            for (int i = 0; i + 2 < newTokens.Count && start < 0; i++)
                if (newTokens[i].Kind == LuaTokenKind.Name && newTokens[i].Text == name && newTokens[i + 1].Is("=") &&
                    newTokens[i + 2].Is("{") && (i == 0 || !newTokens[i - 1].Is(".")))
                    start = i + 2;
        }
        if (start < 0)
            throw new InvalidDataException(name == null
                ? "The file does not return a table."
                : $"The file returns '{name}' but never assigns it a table.");

        root = LuaTableParser.ParseTable(source, newTokens, ref start);
        text = source;
        tokens = newTokens;
        RootName = name;
    }

    // ---------------------------------------------------------------- formatting

    /// <summary>A value as shown to a person: text in quotes, numbers, number lists in braces.</summary>
    public static string DisplayText(LuaValue? value) => value switch
    {
        null => "(not set)",
        LuaString s => $"\"{s.Value}\"",
        LuaNumber n => FormatNumber(n.Value),
        LuaTable t when t.Fields.All(f => f.Value is LuaNumber) =>
            "{" + string.Join(", ", t.Fields.Select(f => FormatNumber(((LuaNumber)f.Value).Value))) + "}",
        LuaTable => "(table)",
        LuaBoolean b => b.Value ? "true" : "false",
        LuaExpression e => e.Text,
        _ => "nil",
    };

    public static string FormatNumber(double value) =>
        double.IsInteger(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    public static string QuoteString(string value, char quote = '"')
    {
        var sb = new StringBuilder().Append(quote);
        foreach (char c in value)
        {
            if (c == quote || c == '\\')
                sb.Append('\\').Append(c);
            else if (c == '\n')
                sb.Append("\\n");
            else if (c == '\r')
                sb.Append("\\r");
            else if (c < 32)
                sb.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }
        return sb.Append(quote).ToString();
    }

    static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    static string KeyText(string key) =>
        key.Length > 0 && (char.IsLetter(key[0]) || key[0] == '_') && key.All(c => char.IsLetterOrDigit(c) || c == '_')
            ? key
            : double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? $"[{key}]" : $"[{QuoteString(key)}]";
}
