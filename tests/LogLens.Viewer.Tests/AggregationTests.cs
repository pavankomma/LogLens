using LogLens.Viewer.Store;
using Xunit;

namespace LogLens.Viewer.Tests;

public class AggregationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private static StoredEvent Ev(int i, string user, string route, double elapsed, LogLevel level = LogLevel.Information, string status = "200") => new()
    {
        Timestamp = T0.AddSeconds(i),
        Level = level,
        Message = $"{route} done",
        Template = "{Route} done",
        Category = "Http",
        Properties = new Dictionary<string, string> { ["User"] = user, ["Route"] = route, ["Elapsed"] = elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Status"] = status },
    };

    private static LogStore Seed()
    {
        var store = new LogStore(1000);
        var events = new List<StoredEvent>();
        for (var i = 0; i < 40; i++) events.Add(Ev(i, i % 2 == 0 ? "ann" : "bob", "/api/a", 100 + i));
        for (var i = 40; i < 50; i++) events.Add(Ev(i, "carol", "/api/b", 1000 + i, i % 2 == 0 ? LogLevel.Error : LogLevel.Warning, "503"));
        store.Add(events);
        return store;
    }

    [Fact]
    public static void Groups_by_property_with_counts_errors_and_last_seen()
    {
        var r = Seed().Aggregate(_ => true, null, null, "User", "Elapsed");
        Assert.Equal(50, r.Total);
        Assert.Equal(3, r.DistinctKeys);
        Assert.Equal(0, r.Unattributed);
        Assert.Equal(new[] { "ann", "bob", "carol" }, r.Groups.Select(g => g.Key));
        var carol = r.Groups.Single(g => g.Key == "carol");
        Assert.Equal(10, carol.Count);
        Assert.Equal(5, carol.Errors);
        Assert.Equal(5, carol.Warnings);
        Assert.Equal(10, carol.Status5xx);
        Assert.Equal(T0.AddSeconds(49), carol.Last);
        Assert.NotNull(carol.LastErrorSeq);
        Assert.Contains("{Route} done", carol.Top);
    }

    [Fact]
    public static void Duration_percentiles_and_slowest()
    {
        var r = Seed().Aggregate(_ => true, null, null, "Route", "Elapsed", buckets: 5);
        var a = r.Groups.Single(g => g.Key == "/api/a");
        // values 100..139 → nearest-rank p50 is the 20th value = 119
        Assert.InRange(a.P50!.Value, 119, 120);
        Assert.InRange(a.P95!.Value, 137, 138);
        Assert.Equal(139, a.Max);
        Assert.Equal(5, a.Trend.Count);
        Assert.Equal(a.Count, a.Trend.Sum());
        var b = r.Groups.Single(g => g.Key == "/api/b");
        Assert.Equal(1049, b.Max);
        Assert.NotNull(b.SlowestSeq);
        Assert.True(b.TrendP95[^1] > 1000);
    }

    [Fact]
    public static void Special_keys_unattributed_and_limits()
    {
        var store = Seed();
        store.Add([new StoredEvent { Timestamp = T0.AddSeconds(60), Level = LogLevel.Error, Message = "orphan" }]);

        var byLevel = store.Aggregate(_ => true, null, null, "level", null);
        Assert.Contains(byLevel.Groups, g => g.Key == "Error" && g.Count == 6);

        var byCat = store.Aggregate(_ => true, null, null, "category", null);
        Assert.Equal(1, byCat.Unattributed);          // the orphan has no category
        Assert.Equal(50, byCat.Groups.Single(g => g.Key == "Http").Count);

        var limited = store.Aggregate(_ => true, null, null, "User", null, limit: 1);
        Assert.Single(limited.Groups);
        Assert.Equal(3, limited.DistinctKeys);
        Assert.Equal(1, limited.Unattributed);

        var filtered = store.Aggregate(e => e.Level >= LogLevel.Error, null, null, "User", null);
        Assert.Equal(new[] { "carol" }, filtered.Groups.Select(g => g.Key));
    }

    [Fact]
    public static void Number_parsing_tolerates_units_and_percentile_edges()
    {
        var e = new StoredEvent { Timestamp = T0, Level = LogLevel.Information, Message = "m", Properties = new Dictionary<string, string> { ["D"] = "12.5 ms" } };
        Assert.Equal(12.5, Aggregation.NumberOf(e, "D"));
        Assert.Null(Aggregation.NumberOf(e, "Missing"));
        Assert.Null(Aggregation.NumberOf(e, null));
        Assert.Equal(5, Aggregation.Percentile([5], 95));
        Assert.True(double.IsNaN(Aggregation.Percentile([], 50)));
    }
}
