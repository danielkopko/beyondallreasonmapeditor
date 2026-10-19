using System.Globalization;
using System.Runtime.CompilerServices;

namespace MapCore.Lua;

/// <summary>An error while running a Lua script: a bad operation, a function that isn't there, or a limit reached.</summary>
public class LuaRuntimeException(string message) : Exception(message);

/// <summary>A script did more work than <see cref="LuaLimits"/> allows (an endless loop, say); <c>pcall</c> can't catch it.</summary>
public sealed class LuaLimitException(string message) : LuaRuntimeException(message);

/// <summary>How much a script may do before it is stopped. Map files are untrusted, so every run is bounded.</summary>
public sealed record LuaLimits(int MaxSteps = 2_000_000, int MaxCallDepth = 100, int MaxTableEntries = 1_000_000, int MaxStringLength = 1 << 20)
{
    public static LuaLimits Default { get; } = new();
}

/// <summary>Counts a run's work against its <see cref="LuaLimits"/>.</summary>
public sealed class LuaBudget(LuaLimits limits)
{
    long steps, entries;

    public LuaLimits Limits { get; } = limits;

    public void Step()
    {
        if (++steps > Limits.MaxSteps)
            throw new LuaLimitException($"the script ran too long (over {Limits.MaxSteps:N0} steps)");
    }

    public void NewEntry()
    {
        if (++entries > Limits.MaxTableEntries)
            throw new LuaLimitException($"the script built too many table entries (over {Limits.MaxTableEntries:N0})");
    }

    public string CheckLength(string text) => text.Length <= Limits.MaxStringLength ? text
        : throw new LuaLimitException($"the script built a string over {Limits.MaxStringLength:N0} characters");
}

/// <summary>A Lua table: keys in the order they were first set (so runs are repeatable), with an optional metatable.</summary>
public sealed class LuaMap(LuaBudget? budget = null)
{
    readonly OrderedDictionary<object, object> entries = new(KeyComparer.Instance);
    static int nextId;

    /// <summary>A number for <c>tostring</c> ("table: 0x…").</summary>
    internal int Id { get; } = Interlocked.Increment(ref nextId);

    public LuaMap? Metatable { get; set; }
    public int Count => entries.Count;

    /// <summary>The entries, in the order their keys were first set.</summary>
    public IEnumerable<KeyValuePair<object, object>> Entries => entries;

    /// <summary>The value at a key, without metatables (nil when absent).</summary>
    public object? Get(object? key) => key != null && entries.TryGetValue(Normalize(key), out var value) ? value : null;

    /// <summary>Sets a key, without metatables; nil removes it.</summary>
    public void Set(object? key, object? value)
    {
        if (key == null)
            throw new LuaRuntimeException("table index is nil");
        if (key is double d && double.IsNaN(d))
            throw new LuaRuntimeException("table index is NaN");
        key = Normalize(key);
        if (value == null)
        {
            entries.Remove(key);
            return;
        }
        if (!entries.ContainsKey(key))
            budget?.NewEntry();
        entries[key] = value;
    }

    /// <summary>The value at a string key, ignoring case (feature definitions aren't consistent about it).</summary>
    public object? GetIgnoreCase(string key)
    {
        if (Get(key) is object exact)
            return exact;
        foreach (var (k, v) in entries)
            if (k is string s && s.Equals(key, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    /// <summary>The border Lua's # operator gives: the count of 1, 2, 3… keys before the first gap.</summary>
    public int Length
    {
        get
        {
            int n = 0;
            while (entries.ContainsKey((double)(n + 1)))
                n++;
            return n;
        }
    }

    /// <summary>The entry after <paramref name="key"/> (the first for nil), or null at the end, as Lua's next.</summary>
    public KeyValuePair<object, object>? Next(object? key)
    {
        int index = 0;
        if (key != null)
        {
            index = entries.IndexOf(Normalize(key));
            if (index < 0)
                throw new LuaRuntimeException("invalid key to 'next'");
            index++;
        }
        return index < entries.Count ? entries.GetAt(index) : null;
    }

    /// <summary>The keys as they are now (for pairs, which may clear entries as it goes).</summary>
    internal object[] SnapshotKeys() => [.. entries.Keys];

    static object Normalize(object key) => key is double d && d == 0 ? 0.0 : key; // -0 and 0 are one key

    sealed class KeyComparer : IEqualityComparer<object>
    {
        public static readonly KeyComparer Instance = new();

        public new bool Equals(object? a, object? b) => a switch
        {
            double x => b is double y && x == y,
            string s => b is string t && string.Equals(s, t, StringComparison.Ordinal),
            bool p => b is bool q && p == q,
            _ => ReferenceEquals(a, b),
        };

        public int GetHashCode(object o) => o switch
        {
            double d => d.GetHashCode(),
            string s => StringComparer.Ordinal.GetHashCode(s),
            bool b => b.GetHashCode(),
            _ => RuntimeHelpers.GetHashCode(o),
        };
    }
}

/// <summary>A Lua function: a script's own (a closure) or one of the few built in.</summary>
public abstract class LuaFunction
{
    static int nextId;
    internal int Id { get; } = Interlocked.Increment(ref nextId);
    public abstract string Name { get; }
}

/// <summary>Value conversions as Lua 5.1 does them.</summary>
public static class LuaValuesRuntime
{
    public static bool IsTruthy(object? value) => value is not (null or false);

    public static string TypeName(object? value) => value switch
    {
        null => "nil",
        bool => "boolean",
        double => "number",
        string => "string",
        LuaMap => "table",
        LuaFunction => "function",
        _ => "userdata",
    };

    /// <summary>
    /// A number as Lua 5.1 prints it ("%.14g"): whole numbers without a decimal point, so <c>"tree_" .. 1</c> is
    /// "tree_1"; very large or small numbers in exponent form ("1e+15").
    /// </summary>
    public static string NumberToString(double d)
    {
        if (double.IsNaN(d))
            return "nan";
        if (double.IsInfinity(d))
            return d > 0 ? "inf" : "-inf";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e14)
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        var s = d.ToString("G14", CultureInfo.InvariantCulture);
        int e = s.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
            return s;
        int exponent = int.Parse(s.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{s[..e]}e{(exponent < 0 ? '-' : '+')}{Math.Abs(exponent):00}");
    }

    public static string ToDisplayString(object? value) => value switch
    {
        null => "nil",
        bool b => b ? "true" : "false",
        double d => NumberToString(d),
        string s => s,
        LuaMap t => $"table: 0x{t.Id:x8}",
        LuaFunction f => $"function: 0x{f.Id:x8}",
        _ => value.ToString() ?? "",
    };

    /// <summary>A number from a number or numeric text (as Lua's arithmetic and tonumber read them), else null.</summary>
    public static double? ToNumber(object? value) => value switch
    {
        double d => d,
        string s => ParseNumber(s),
        _ => null,
    };

    public static double? ParseNumber(string text)
    {
        var s = text.Trim();
        if (s.Length == 0)
            return null;
        bool negative = s[0] == '-';
        var body = s.TrimStart('-', '+');
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(body.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long hex) && s.Length - body.Length <= 1
                ? (negative ? -hex : hex) : null;
        if (body.Length > 0 && !char.IsDigit(body[0]) && body[0] != '.')
            return null; // "inf", "nan" and the like aren't numbers in Lua source or tonumber
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}
