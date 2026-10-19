using System.Globalization;

namespace MapCore.Lua;

/// <summary>A value in a Lua table constructor, with the position of its source text.</summary>
public abstract record LuaValue(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record LuaNumber(double Value, int Start, int Length) : LuaValue(Start, Length);

/// <param name="Quote">'"' or '\'' for quoted strings, '[' for long strings.</param>
public sealed record LuaString(string Value, char Quote, int Start, int Length) : LuaValue(Start, Length);

public sealed record LuaBoolean(bool Value, int Start, int Length) : LuaValue(Start, Length);

public sealed record LuaNil(int Start, int Length) : LuaValue(Start, Length);

/// <summary>Anything that is not a plain literal or table (arithmetic, variables, calls); kept as source text.</summary>
public sealed record LuaExpression(string Text, int Start, int Length) : LuaValue(Start, Length);

/// <summary>One field of a table constructor: <c>name = v</c>, <c>[key] = v</c>, or a positional <c>v</c>.</summary>
/// <param name="Start">Where the field's text begins (its key, or its value for positional fields).</param>
public sealed record LuaField(string? Name, LuaValue? Key, int? Position, LuaValue Value, int Start)
{
    /// <summary>Does this field answer to <paramref name="key"/>? Names and string keys match case-insensitively.</summary>
    public bool Matches(string key)
    {
        if (Name != null)
            return Name.Equals(key, StringComparison.OrdinalIgnoreCase);
        if (Key is LuaString s)
            return s.Value.Equals(key, StringComparison.OrdinalIgnoreCase);
        if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return Key is LuaNumber n ? n.Value == number : Position == number;
        return false;
    }

    /// <summary>The key as written in a path ("startPos", "0", ...).</summary>
    public string KeyText => Name ?? Key switch
    {
        LuaString s => s.Value,
        LuaNumber n => n.Value.ToString(CultureInfo.InvariantCulture),
        LuaExpression e => $"[{e.Text}]",
        _ => Position?.ToString(CultureInfo.InvariantCulture) ?? "?",
    };
}

public sealed record LuaTable(IReadOnlyList<LuaField> Fields, int Start, int Length) : LuaValue(Start, Length)
{
    /// <summary>The field for <paramref name="key"/>; when a key is repeated the last one wins, as in Lua.</summary>
    public LuaField? Field(string key) => Fields.LastOrDefault(f => f.Matches(key));

    public LuaValue? this[string key] => Field(key)?.Value;
}
