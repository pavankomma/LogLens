using System.Globalization;
using System.Text.Json;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Ingest;

/// <summary>Parses LogLens <c>JsonFormatter</c> output (and tolerant variants) into <see cref="StoredEvent"/>s.</summary>
public static partial class EventParser
{
    /// <summary>Parses a body that is either NDJSON or a JSON array. Malformed lines are skipped.</summary>
    public static List<StoredEvent> ParseMany(string body, string? source, DateTimeOffset? fallbackTime = null)
    {
        var result = new List<StoredEvent>();
        if (string.IsNullOrWhiteSpace(body)) return result;

        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '[')
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                foreach (var el in doc.RootElement.EnumerateArray())
                    if (TryParse(el, source, fallbackTime, out var e)) result.Add(e);
            }
            catch (JsonException) { /* fall through to line parsing */ }

            if (result.Count > 0) return result;
        }

        foreach (var line in body.Split('\n'))
        {
            if (TryParseLine(line, source, fallbackTime, out var e)) result.Add(e);
        }

        return result;
    }

    /// <summary>Parses a single NDJSON line.</summary>
    public static bool TryParseLine(string line, string? source, DateTimeOffset? fallbackTime, out StoredEvent stored)
    {
        stored = null!;
        var t = line.Trim().TrimEnd(',');
        if (t.Length < 2 || t[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(t);
            return TryParse(doc.RootElement, source, fallbackTime, out stored);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParse(JsonElement el, string? source, DateTimeOffset? fallbackTime, out StoredEvent stored)
    {
        stored = null!;
        if (el.ValueKind != JsonValueKind.Object) return false;

        var timestamp = fallbackTime ?? DateTimeOffset.UtcNow;
        if (TryGet(el, out var ts, "timestamp", "@t", "Timestamp", "time") && ts.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            timestamp = parsed;

        var level = LogLevel.Information;
        if (TryGet(el, out var lv, "level", "@l", "Level") && lv.ValueKind == JsonValueKind.String)
            level = ParseLevel(lv.GetString());

        string? message = TryGet(el, out var m, "message", "@m", "Message", "msg") ? AsString(m) : null;
        string? template = TryGet(el, out var tp, "template", "@mt", "MessageTemplate") ? AsString(tp) : null;
        string? category = TryGet(el, out var c, "category", "Category", "SourceContext", "logger") ? AsString(c) : null;
        string? exception = TryGet(el, out var ex, "exception", "@x", "Exception") ? AsString(ex) : null;

        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryGet(el, out var p, "properties", "Properties") && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var kv in p.EnumerateObject()) props[kv.Name] = AsString(kv.Value) ?? "null";
        }
        else
        {
            // Flat shapes (e.g. Serilog compact): everything not recognised is a property.
            foreach (var kv in el.EnumerateObject())
            {
                if (kv.Name is "timestamp" or "@t" or "level" or "@l" or "message" or "@m" or "template" or "@mt"
                    or "category" or "exception" or "@x" or "@i" or "@r" or "@tr" or "@sp" or "@ps" or "@st" or "@sa" or "@ra" or "@sk"
                    or "Timestamp" or "Level" or "Message" or "MessageTemplate" or "Exception")
                    continue;
                props[kv.Name] = AsString(kv.Value) ?? "null";
            }
        }

        // CLEF extras: keep trace/span ids as ordinary properties so they work as correlation keys.
        if (el.TryGetProperty("@tr", out var tr) && tr.ValueKind == JsonValueKind.String) props["TraceId"] = tr.GetString()!;
        if (el.TryGetProperty("@sp", out var sp) && sp.ValueKind == JsonValueKind.String) props["SpanId"] = sp.GetString()!;
        if (el.TryGetProperty("@ps", out var ps) && ps.ValueKind == JsonValueKind.String) props["ParentSpanId"] = ps.GetString()!;
        if (el.TryGetProperty("@i", out var evid)) props["EventId"] = AsString(evid) ?? "";
        if (category is null && props.TryGetValue("Category", out var cat)) category = cat;
        if (message is null) message = template is null ? string.Empty : RenderTemplate(template, props);
        if (message.Length == 0 && props.Count == 0 && exception is null) return false;

        stored = new StoredEvent
        {
            Timestamp = timestamp,
            Level = level,
            Message = message,
            Template = template,
            Category = category,
            Exception = exception,
            Properties = props,
            Source = source,
        };
        return true;
    }

    /// <summary>Renders a message template (<c>{Name}</c>, <c>{@Name}</c>, <c>{Name:format}</c>) from flattened properties; unknown holes stay literal.</summary>
    public static string RenderTemplate(string template, IReadOnlyDictionary<string, string> props)
    {
        if (template.IndexOf('{') < 0) return template;
        return TemplateHole().Replace(template, m =>
        {
            if (m.Value == "{{") return "{";
            if (m.Value == "}}") return "}";
            return props.TryGetValue(m.Groups["name"].Value, out var v) ? v : m.Value;
        });
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\{\{|\}\}|\{[@$]?(?<name>[A-Za-z0-9_]+)(?:,-?\d+)?(?::[^}]*)?\}")]
    private static partial System.Text.RegularExpressions.Regex TemplateHole();

    /// <summary>Maps level names and common aliases; unknown values become Information.</summary>
    public static LogLevel ParseLevel(string? s)
    {
        if (string.IsNullOrEmpty(s)) return LogLevel.Information;
        switch (s!.Trim().ToLowerInvariant())
        {
            case "trace" or "trc" or "verbose" or "vrb": return LogLevel.Trace;
            case "debug" or "dbg": return LogLevel.Debug;
            case "information" or "info" or "inf": return LogLevel.Information;
            case "warning" or "warn" or "wrn": return LogLevel.Warning;
            case "error" or "err" or "eror": return LogLevel.Error;
            case "fatal" or "ftl" or "critical" or "crit": return LogLevel.Fatal;
            default: return LogLevel.Information;
        }
    }

    private static bool TryGet(JsonElement el, out JsonElement value, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out value)) return true;
        value = default;
        return false;
    }

    private static string? AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => v.GetRawText(),
        _ => v.GetRawText(),
    };
}
