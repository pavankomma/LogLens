using System.Globalization;

namespace LogLens.Viewer.Store;

/// <summary>One group in an aggregation (e.g. one user, one route, one resource).</summary>
public sealed class AggregateGroup
{
    /// <summary>The group key value.</summary>
    public required string Key { get; init; }
    /// <summary>Matching entries.</summary>
    public int Count { get; set; }
    /// <summary>Entries at Error or above.</summary>
    public int Errors { get; set; }
    /// <summary>Warnings.</summary>
    public int Warnings { get; set; }
    /// <summary>First matching entry.</summary>
    public DateTimeOffset First { get; set; }
    /// <summary>Most recent matching entry.</summary>
    public DateTimeOffset Last { get; set; }
    /// <summary>Median of the duration property (null if none).</summary>
    public double? P50 { get; set; }
    /// <summary>95th percentile of the duration property.</summary>
    public double? P95 { get; set; }
    /// <summary>Maximum duration.</summary>
    public double? Max { get; set; }
    /// <summary>Entries whose <c>Status</c>-like property is ≥ 500.</summary>
    public int Status5xx { get; set; }
    /// <summary>Most frequent message templates (or messages), best first.</summary>
    public IReadOnlyList<string> Top { get; set; } = [];
    /// <summary>Entries per time bucket.</summary>
    public IReadOnlyList<int> Trend { get; set; } = [];
    /// <summary>P95 duration per time bucket (null where no durations).</summary>
    public IReadOnlyList<double?> TrendP95 { get; set; } = [];
    /// <summary>Sequence of the slowest entry (for "open it").</summary>
    public long? SlowestSeq { get; set; }
    /// <summary>Sequence of the most recent error.</summary>
    public long? LastErrorSeq { get; set; }
}

/// <summary>Result of <see cref="LogStore.Aggregate"/>.</summary>
public sealed class AggregateResult
{
    /// <summary>Groups, ordered by count descending.</summary>
    public required IReadOnlyList<AggregateGroup> Groups { get; init; }
    /// <summary>Distinct group keys seen (before <c>limit</c>).</summary>
    public required int DistinctKeys { get; init; }
    /// <summary>Entries that matched but had no value for the group key.</summary>
    public required int Unattributed { get; init; }
    /// <summary>Total matching entries.</summary>
    public required int Total { get; init; }
    /// <summary>Range start used for buckets.</summary>
    public required DateTimeOffset RangeStart { get; init; }
    /// <summary>Range end used for buckets.</summary>
    public required DateTimeOffset RangeEnd { get; init; }
}

/// <summary>Grouping / percentile helpers.</summary>
public static class Aggregation
{
    internal static readonly string[] StatusKeys = ["Status", "StatusCode", "status", "status_code", "http.status_code", "ResponseStatus"];

    /// <summary>Resolves the group key from a stored event: <c>level</c>, <c>category</c>, <c>source</c>, or a property name.</summary>
    public static string? KeyOf(StoredEvent e, string by)
    {
        if (by.Equals("level", StringComparison.OrdinalIgnoreCase)) return e.Level.ToString();
        if (by.Equals("category", StringComparison.OrdinalIgnoreCase)) return e.Category;
        if (by.Equals("source", StringComparison.OrdinalIgnoreCase)) return e.Source;
        if (e.Properties.TryGetValue(by, out var v)) return v;
        foreach (var kv in e.Properties) if (kv.Key.Equals(by, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    /// <summary>Parses a numeric property (invariant culture); tolerates a trailing unit like <c>ms</c>.</summary>
    public static double? NumberOf(StoredEvent e, string? key)
    {
        if (key is null || !e.Properties.TryGetValue(key, out var raw)) return null;
        var s = raw.Trim();
        var end = s.Length;
        while (end > 0 && !(char.IsDigit(s[end - 1]) || s[end - 1] == '.')) end--;
        return double.TryParse(s.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Nearest-rank percentile of a sorted list.</summary>
    public static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return double.NaN;
        var rank = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    internal static bool Is5xx(StoredEvent e)
    {
        foreach (var k in StatusKeys)
            if (e.Properties.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) return code >= 500;
        return false;
    }
}
