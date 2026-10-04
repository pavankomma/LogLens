using System.Globalization;
using LogLens.Viewer.Ingest;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Query;

/// <summary>
/// Parses the viewer query DSL into a predicate. Terms are ANDed; the parser never throws — anything it does not
/// understand becomes a free-text term.
/// </summary>
public static class QueryParser
{
    private static readonly char[] Operators = ['!', '=', '~', '>', '<'];

    /// <summary>Parses <paramref name="query"/> into a predicate; an empty query matches everything.</summary>
    public static Func<StoredEvent, bool> Parse(string? query)
    {
        var terms = Tokenize(query ?? string.Empty).Select(BuildTerm).ToArray();
        if (terms.Length == 0) return static _ => true;
        return e =>
        {
            foreach (var t in terms) if (!t(e)) return false;
            return true;
        };
    }

    /// <summary>Splits on whitespace, honouring double quotes (which may appear after an operator: <c>Name="a b"</c>).</summary>
    public static List<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var ch in query)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
                continue;
            }

            cur.Append(ch);
        }

        if (cur.Length > 0) tokens.Add(cur.ToString());
        return tokens;
    }

    private static Func<StoredEvent, bool> BuildTerm(string raw)
    {
        var negate = raw.Length > 1 && raw[0] == '-';
        var term = negate ? raw.Substring(1) : raw;
        var inner = BuildPositive(term);
        return negate ? e => !inner(e) : inner;
    }

    private static Func<StoredEvent, bool> BuildPositive(string term)
    {
        var colon = term.IndexOf(':');
        if (colon > 0)
        {
            var key = term.Substring(0, colon).ToLowerInvariant();
            var value = term.Substring(colon + 1);
            switch (key)
            {
                case "level":
                    return LevelTerm(value);
                case "category":
                    return e => Wildcard(e.Category, value);
                case "has":
                    if (value.Equals("exception", StringComparison.OrdinalIgnoreCase)) return e => e.Exception is not null;
                    return e => e.Properties.ContainsKey(value) || e.Properties.Keys.Any(k => k.Equals(value, StringComparison.OrdinalIgnoreCase));
                case "source":
                    return e => Wildcard(e.Source, value);
                case "message":
                    return e => Contains(e.Message, value);
                case "exception":
                    return e => Contains(e.Exception, value);
            }
        }

        var opIndex = term.IndexOfAny(Operators);
        if (opIndex > 0)
        {
            var key = term.Substring(0, opIndex);
            var rest = term.Substring(opIndex);
            string op;
            if (rest.StartsWith("!=", StringComparison.Ordinal) || rest.StartsWith(">=", StringComparison.Ordinal) || rest.StartsWith("<=", StringComparison.Ordinal)) op = rest.Substring(0, 2);
            else op = rest.Substring(0, 1);
            var value = rest.Substring(op.Length);

            return op switch
            {
                "=" => e => TryProp(e, key, out var v) && v.Equals(value, StringComparison.OrdinalIgnoreCase),
                "!=" => e => !(TryProp(e, key, out var v) && v.Equals(value, StringComparison.OrdinalIgnoreCase)),
                "~" => e => TryProp(e, key, out var v) && Contains(v, value),
                ">" => Numeric(key, ">", value, static (a, b) => a > b),
                "<" => Numeric(key, "<", value, static (a, b) => a < b),
                ">=" => Numeric(key, ">=", value, static (a, b) => a >= b),
                "<=" => Numeric(key, "<=", value, static (a, b) => a <= b),
                _ => TextTerm(term),
            };
        }

        return TextTerm(term);
    }

    private static Func<StoredEvent, bool> LevelTerm(string value)
    {
        var atLeast = value.EndsWith('+');
        var name = atLeast ? value.Substring(0, value.Length - 1) : value;
        var level = EventParser.ParseLevel(name);
        if (name.Length == 0) return static _ => true;
        return atLeast ? e => e.Level >= level : e => e.Level == level;
    }

    private static Func<StoredEvent, bool> Numeric(string key, string op, string value, Func<double, double, bool> cmp)
    {
        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var rhs)) return TextTerm(key + op + value);
        return e => TryProp(e, key, out var v) && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var lhs) && cmp(lhs, rhs);
    }

    private static Func<StoredEvent, bool> TextTerm(string text) => e =>
        Contains(e.Message, text) || Contains(e.Template, text) || Contains(e.Category, text) ||
        Contains(e.Exception, text) || e.Properties.Values.Any(v => Contains(v, text));

    private static bool TryProp(StoredEvent e, string key, out string value)
    {
        if (e.Properties.TryGetValue(key, out value!)) return true;
        foreach (var kv in e.Properties)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; return true; }
        }

        if (key.Equals("category", StringComparison.OrdinalIgnoreCase) && e.Category is not null) { value = e.Category; return true; }
        value = string.Empty;
        return false;
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool Wildcard(string? value, string pattern)
    {
        if (value is null) return false;
        var starts = pattern.StartsWith('*');
        var ends = pattern.EndsWith('*');
        var core = pattern.Trim('*');
        if (core.Length == 0) return true;
        if (starts && ends) return Contains(value, core);
        if (starts) return value.EndsWith(core, StringComparison.OrdinalIgnoreCase);
        if (ends) return value.StartsWith(core, StringComparison.OrdinalIgnoreCase);
        return value.Equals(core, StringComparison.OrdinalIgnoreCase);
    }
}
