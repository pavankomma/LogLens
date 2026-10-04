using LogLens.Viewer.Store;
using Xunit;

namespace LogLens.Viewer.Tests;

public class LogStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private static StoredEvent Ev(int i, LogLevel level = LogLevel.Information, string cat = "A") => new()
    {
        Timestamp = T0.AddSeconds(i),
        Level = level,
        Message = "m" + i,
        Category = cat,
        Properties = new Dictionary<string, string> { ["I"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Parity"] = i % 2 == 0 ? "even" : "odd" },
    };

    [Fact]
    public static void Ring_buffer_evicts_oldest_and_assigns_monotonic_seq()
    {
        var store = new LogStore(3);
        store.Add([Ev(1), Ev(2)]);
        store.Add([Ev(3), Ev(4)]);
        Assert.Equal(3, store.Count);
        Assert.Equal(4, store.IngestedTotal);
        var r = store.Query(_ => true, null, null, 10, null);
        Assert.Equal(new[] { "m4", "m3", "m2" }, r.Events.Select(e => e.Message));
        Assert.Equal(new long[] { 4, 3, 2 }, r.Events.Select(e => e.Seq));
        Assert.Null(store.Get(1));
        Assert.Equal("m3", store.Get(3)!.Message);
        Assert.Equal((T0.AddSeconds(2), T0.AddSeconds(4)), store.Range());
        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Null(store.Range());
    }

    [Fact]
    public static void Query_limits_pages_and_counts_total_and_facets()
    {
        var store = new LogStore(100);
        store.Add(Enumerable.Range(1, 50).Select(i => Ev(i, i % 10 == 0 ? LogLevel.Error : LogLevel.Information, i % 3 == 0 ? "B" : "A")));

        var page1 = store.Query(_ => true, null, null, 20, null);
        Assert.Equal(50, page1.Total);
        Assert.Equal(20, page1.Events.Count);
        Assert.Equal("m50", page1.Events[0].Message);

        var page2 = store.Query(_ => true, null, null, 20, page1.Events[^1].Seq);
        Assert.Equal("m30", page2.Events[0].Message);
        Assert.Equal(50, page2.Total);

        Assert.Equal(5, page1.Levels["Error"]);
        Assert.Equal(45, page1.Levels["Information"]);
        Assert.Equal(16, page1.Categories["B"]);
        Assert.Equal(25, page1.Properties["Parity"]["even"]);
        Assert.False(page1.Properties.ContainsKey("I")); // all-unique values are not offered as a pivot

        var errors = store.Query(e => e.Level == LogLevel.Error, null, null, 20, null);
        Assert.Equal(5, errors.Total);
        Assert.Single(errors.Levels);
    }

    [Fact]
    public static void Time_range_and_histogram()
    {
        var store = new LogStore(100);
        store.Add(Enumerable.Range(0, 60).Select(i => Ev(i)));
        var r = store.Query(_ => true, T0.AddSeconds(10), T0.AddSeconds(19), 100, null, histogramBuckets: 10);
        Assert.Equal(10, r.Total);
        Assert.Equal(10, r.Histogram.Count);
        Assert.Equal(10, r.Histogram.Sum(b => b.Count));
        Assert.Equal(T0.AddSeconds(10), r.RangeStart);
        Assert.Equal(T0.AddSeconds(19), r.RangeEnd);

        var open = store.Query(_ => true, null, null, 100, null, histogramBuckets: 6);
        Assert.Equal(T0, open.RangeStart);
        Assert.Equal(T0.AddSeconds(59), open.RangeEnd);
        Assert.All(open.Histogram, b => Assert.Equal(10, b.Count));

        var empty = new LogStore(5).Query(_ => true, null, null, 10, null, 4);
        Assert.Equal(0, empty.Total);
        Assert.Equal(4, empty.Histogram.Count);
    }

    [Fact]
    public static async Task Subscribers_receive_new_events_until_cancelled()
    {
        var store = new LogStore(10);
        using var cts = new CancellationTokenSource();
        var reader = store.Subscribe(cts.Token);
        store.Add([Ev(1), Ev(2)]);
        Assert.Equal("m1", (await reader.ReadAsync(cts.Token)).Message);
        Assert.Equal("m2", (await reader.ReadAsync(cts.Token)).Message);
        cts.Cancel();
        store.Add([Ev(3)]);
        Assert.False(await reader.WaitToReadAsync(CancellationToken.None));
    }

    [Fact]
    public static void Warning_and_above_evicted_events_move_to_protected_buffer()
    {
        var store = new LogStore(3, protectedCapacity: 2);
        store.Add([Ev(1, LogLevel.Error), Ev(2), Ev(3), Ev(4), Ev(5)]);
        // m1 (Error) survives eviction in the protected buffer; m2 (Information) is dropped.
        Assert.Equal(4, store.Count);
        Assert.Equal(1, store.ProtectedCount);
        var r = store.Query(_ => true, null, null, 10, null);
        Assert.Equal(new[] { "m5", "m4", "m3", "m1" }, r.Events.Select(e => e.Message));
        Assert.Equal("m1", store.Get(1)!.Message);
        Assert.Null(store.Get(2));
        Assert.Equal((T0.AddSeconds(1), T0.AddSeconds(5)), store.Range());
    }

    [Fact]
    public static void Protected_buffer_is_fifo_and_can_be_disabled()
    {
        var store = new LogStore(2, protectedCapacity: 1);
        store.Add([Ev(1, LogLevel.Warning), Ev(2, LogLevel.Error), Ev(3, LogLevel.Fatal), Ev(4, LogLevel.Error)]);
        // m1 then m2 fall off the ring; the 1-slot protected buffer keeps only the newer m2.
        var r = store.Query(_ => true, null, null, 10, null);
        Assert.Equal(new[] { "m4", "m3", "m2" }, r.Events.Select(e => e.Message));
        Assert.Null(store.Get(1));

        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.ProtectedCount);

        var off = new LogStore(2, protectedCapacity: 0);
        off.Add([Ev(1, LogLevel.Error), Ev(2), Ev(3)]);
        Assert.Equal(2, off.Count);
        Assert.Null(off.Get(1));
    }

    [Fact]
    public static void Invalid_capacity_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogStore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogStore(3, -1));
    }
}
