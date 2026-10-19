using System.Diagnostics;
using System.Reflection;

namespace MapCore.Tests;

/// <summary>Marks a public static parameterless method as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
sealed class TestAttribute : Attribute;

sealed class TestFailure(string message) : Exception(message);

static class Report
{
    /// <summary>Prints a measurement; it appears just above the test's PASS/FAIL line.</summary>
    public static void Info(string message) => Console.WriteLine($"      info: {message}");
}

static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure($"{what}: expected {expected}, got {actual}");
    }

    public static void Empty(IReadOnlyCollection<string> problems, string what)
    {
        if (problems.Count > 0)
            throw new TestFailure($"{what}: {problems.Count} problem(s)\n        " + string.Join("\n        ", problems.Take(20)));
    }

    public static TException Throws<TException>(Action action, string what) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        throw new TestFailure($"{what}: expected {typeof(TException).Name}, but nothing was thrown");
    }
}

static class TestRunner
{
    /// <summary>Runs every [Test] method. Optional args filter by substring of "Class.Method".</summary>
    static int Main(string[] args)
    {
        var tests = typeof(TestRunner).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Select(m => (Name: $"{m.DeclaringType!.Name}.{m.Name}", Method: m))
            .Where(t => args.Length == 0 || args.Any(f => t.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Name)
            .ToList();

        if (tests.Count == 0)
        {
            Console.Error.WriteLine($"No tests match filter: {string.Join(", ", args)}");
            return 1;
        }

        int failed = 0;
        foreach (var (name, method) in tests)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                method.Invoke(null, null);
                Console.WriteLine($"PASS  {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                var inner = ex.InnerException!;
                var detail = inner is TestFailure ? inner.Message : inner.ToString();
                Console.WriteLine($"FAIL  {name} ({sw.ElapsedMilliseconds} ms)\n      {detail}");
            }
        }

        Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }
}
