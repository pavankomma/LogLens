using System.Threading.Channels;

namespace LogLens.Viewer.Store;

/// <summary>
/// Bounded in-memory ring buffer of events with query, facets, histogram and live subscriptions.
/// Warning-and-above events evicted from the ring get a second life in a smaller protected buffer,
/// so a flood of verbose events cannot flush out the problems worth keeping.
/// </summary>
public sealed class LogStore
{
    private readonly StoredEvent?[] _ring;
    private readonly StoredEvent?[] _protected;   // warn+ events that fell off the main ring, FIFO
    private readonly object _sync = new();
    private readonly List<ChannelWriter<StoredEvent>> _subscribers = [];
    private long _nextSeq = 1;
    private int _head;       // index of the next write
    private int _count;
    private int _pHead;
    private int _pCount;
    private long _ingestedTotal;

    /// <summary>Creates a store holding at most <paramref name="capacity"/> events.</summary>
    /// <param name="capacity">Main ring capacity.</param>
    /// <param name="protectedCapacity">
    /// Extra slots retaining Warning+ events evicted from the ring; 0 disables, null means capacity / 8 (at least 1).
    /// </param>
    public LogStore(int capacity = 200_000, int? protectedCapacity = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (protectedCapacity is < 0) throw new ArgumentOutOfRangeException(nameof(protectedCapacity));
        _ring = new StoredEvent?[capacity];
        _protected = new StoredEvent?[protectedCapacity ?? Math.Max(1, capacity / 8)];
    }

    /// <summary>Maximum events retained in the main ring.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Maximum Warning+ events retained after eviction from the main ring.</summary>
    public int ProtectedCapacity => _protected.Length;

    /// <summary>Warning+ events currently held in the protected buffer.</summary>
    public int ProtectedCount { get { lock (_sync) return _pCount; } }

    /// <summary>Events currently retained (main ring + protected buffer).</summary>
    public int Count { get { lock (_sync) return _count + _pCount; } }

    /// <summary>Total events ever ingested.</summary>
    public long IngestedTotal => Interlocked.Read(ref _ingestedTotal);

    /// <summary>Adds events, assigning sequence numbers, and notifies subscribers.</summary>
    public int Add(IEnumerable<StoredEvent> events)
    {
        var added = new List<StoredEvent>();
        lock (_sync)
        {
            foreach (var e in events)
            {
                e.Seq = _nextSeq++;
                var evicted = _count == _ring.Length ? _ring[_head] : null;
                _ring[_head] = e;
                _head = (_head + 1) % _ring.Length;
                if (_count < _ring.Length) _count++;
                if (evicted is { } old && old.Level >= LogLevel.Warning && old.Level < LogLevel.None && _protected.Length > 0)
                {
                    _protected[_pHead] = evicted;
                    _pHead = (_pHead + 1) % _protected.Length;
                    if (_pCount < _protected.Length) _pCount++;
                }

                added.Add(e);
            }
        }

        if (added.Count == 0) return 0;
        Interlocked.Add(ref _ingestedTotal, added.Count);

        ChannelWriter<StoredEvent>[] subs;
        lock (_subscribers) subs = _subscribers.ToArray();
        foreach (var sub in subs)
            foreach (var e in added) sub.TryWrite(e);
        return added.Count;
    }

    /// <summary>Removes all events.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_ring);
            Array.Clear(_protected);
            _head = 0;
            _count = 0;
            _pHead = 0;
            _pCount = 0;
        }
    }

    /// <summary>Finds one event by sequence.</summary>
    public StoredEvent? Get(long seq)
    {
        foreach (var e in Snapshot())
            if (e.Seq == seq) return e;
        return null;
    }

    /// <summary>Oldest and newest timestamps, or null when empty.</summary>
    public (DateTimeOffset Oldest, DateTimeOffset Newest)? Range()
    {
        lock (_sync)
        {
            if (_count == 0 && _pCount == 0) return null;
            var oldest = _pCount > 0 ? PAt(0).Timestamp : At(0).Timestamp;
            var newest = _count > 0 ? At(_count - 1).Timestamp : PAt(_pCount - 1).Timestamp;
            return (oldest, newest);
        }
    }

    /// <summary>Runs a query newest-first.</summary>
    public QueryResult Query(Func<StoredEvent, bool> predicate, DateTimeOffset? from, DateTimeOffset? to, int limit, long? before, int histogramBuckets = 60)
    {
        var snapshot = Snapshot();

        var hits = new List<StoredEvent>(Math.Min(limit, 1024));
        var levels = new Dictionary<string, int>();
        var categories = new Dictionary<string, int>();
        var props = new Dictionary<string, Dictionary<string, int>>();
        var total = 0;
        DateTimeOffset? minTs = null, maxTs = null;
        var matchedTimes = new List<(DateTimeOffset Time, LogLevel Level)>();

        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            var e = snapshot[i];
            if (from is { } f && e.Timestamp < f) continue;
            if (to is { } t && e.Timestamp > t) continue;
            if (!predicate(e)) continue;

            total++;
            matchedTimes.Add((e.Timestamp, e.Level));
            if (minTs is null || e.Timestamp < minTs) minTs = e.Timestamp;
            if (maxTs is null || e.Timestamp > maxTs) maxTs = e.Timestamp;

            Bump(levels, e.Level.ToString());
            if (e.Category is not null) Bump(categories, e.Category);
            foreach (var kv in e.Properties)
            {
                if (!props.TryGetValue(kv.Key, out var values))
                {
                    if (props.Count >= 40) continue;
                    props[kv.Key] = values = new Dictionary<string, int>();
                }

                if (values.Count < 200 || values.ContainsKey(kv.Value)) Bump(values, kv.Value);
            }

            if (hits.Count < limit && (before is null || e.Seq < before)) hits.Add(e);
        }

        var rangeStart = from ?? minTs ?? DateTimeOffset.UtcNow;
        var rangeEnd = to ?? maxTs ?? rangeStart;
        var histogram = BuildHistogram(matchedTimes, rangeStart, rangeEnd, histogramBuckets);

        return new QueryResult
        {
            Events = hits,
            Total = total,
            Levels = levels,
            Categories = Top(categories, 10),
            // Skip identifier-like keys (every value unique) — they are noise as pivots.
            Properties = props.Where(p => total < 2 || (p.Value.Values.Max() > 1 && p.Value.Count * 10 < total * 3 + 10))
                .OrderByDescending(p => p.Value.Values.Sum()).Take(20)
                .ToDictionary(p => p.Key, p => Top(p.Value, 10)),
            Histogram = histogram,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
        };
    }

    /// <summary>
    /// Groups matching events by <paramref name="by"/> (level/category/source or a property) with counts, error rates,
    /// duration percentiles over <paramref name="durationKey"/>, and per-bucket trends.
    /// </summary>
    public AggregateResult Aggregate(Func<StoredEvent, bool> predicate, DateTimeOffset? from, DateTimeOffset? to, string by, string? durationKey, int limit = 50, int buckets = 8, int topN = 3)
    {
        var snapshot = Snapshot();

        var matched = new List<StoredEvent>(1024);
        DateTimeOffset? minTs = null, maxTs = null;
        foreach (var e in snapshot)
        {
            if (from is { } f && e.Timestamp < f) continue;
            if (to is { } t && e.Timestamp > t) continue;
            if (!predicate(e)) continue;
            matched.Add(e);
            if (minTs is null || e.Timestamp < minTs) minTs = e.Timestamp;
            if (maxTs is null || e.Timestamp > maxTs) maxTs = e.Timestamp;
        }

        var rangeStart = from ?? minTs ?? DateTimeOffset.UtcNow;
        var rangeEnd = to ?? maxTs ?? rangeStart;
        var span = rangeEnd - rangeStart;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromSeconds(Math.Max(1, buckets));
        var width = Math.Max(1, span.Ticks / Math.Max(1, buckets));

        var groups = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var unattributed = 0;
        foreach (var e in matched)
        {
            var key = Aggregation.KeyOf(e, by);
            if (string.IsNullOrEmpty(key)) { unattributed++; continue; }
            if (!groups.TryGetValue(key!, out var acc)) groups[key!] = acc = new Acc(key!, buckets);
            var b = Math.Clamp((int)((e.Timestamp - rangeStart).Ticks / width), 0, buckets - 1);
            acc.Add(e, b, durationKey, topN);
        }

        var ordered = groups.Values.OrderByDescending(g => g.Count).ThenBy(g => g.Key, StringComparer.Ordinal).Take(limit)
            .Select(g => g.ToGroup(topN)).ToList();
        return new AggregateResult { Groups = ordered, DistinctKeys = groups.Count, Unattributed = unattributed, Total = matched.Count, RangeStart = rangeStart, RangeEnd = rangeEnd };
    }

    private sealed class Acc(string key, int buckets)
    {
        private readonly List<double> _durations = [];
        private readonly List<double>?[] _bucketDurations = new List<double>?[buckets];
        private readonly Dictionary<string, int> _top = new(StringComparer.Ordinal);
        private readonly int[] _trend = new int[buckets];
        private double _max = double.NegativeInfinity;

        public string Key { get; } = key;
        public int Count { get; private set; }
        private int _errors, _warnings, _s5xx;
        private DateTimeOffset _first = DateTimeOffset.MaxValue, _last = DateTimeOffset.MinValue;
        private long? _slowestSeq, _lastErrorSeq;
        private DateTimeOffset _lastErrorAt = DateTimeOffset.MinValue;

        public void Add(StoredEvent e, int bucket, string? durationKey, int topN)
        {
            Count++;
            _trend[bucket]++;
            if (e.Level >= LogLevel.Error) { _errors++; if (e.Timestamp >= _lastErrorAt) { _lastErrorAt = e.Timestamp; _lastErrorSeq = e.Seq; } }
            else if (e.Level == LogLevel.Warning) _warnings++;
            if (Aggregation.Is5xx(e)) _s5xx++;
            if (e.Timestamp < _first) _first = e.Timestamp;
            if (e.Timestamp > _last) _last = e.Timestamp;
            var d = Aggregation.NumberOf(e, durationKey);
            if (d is { } v)
            {
                _durations.Add(v);
                (_bucketDurations[bucket] ??= []).Add(v);
                if (v > _max) { _max = v; _slowestSeq = e.Seq; }
            }

            var label = e.Template ?? e.Message;
            if (label.Length > 0 && (_top.Count < 200 || _top.ContainsKey(label))) _top[label] = _top.TryGetValue(label, out var n) ? n + 1 : 1;
        }

        public AggregateGroup ToGroup(int topN)
        {
            _durations.Sort();
            var trendP95 = new double?[_trend.Length];
            for (var i = 0; i < _trend.Length; i++)
            {
                var bd = _bucketDurations[i];
                if (bd is { Count: > 0 }) { bd.Sort(); trendP95[i] = Aggregation.Percentile(bd, 95); }
            }

            return new AggregateGroup
            {
                Key = Key,
                Count = Count,
                Errors = _errors,
                Warnings = _warnings,
                First = _first,
                Last = _last,
                P50 = _durations.Count > 0 ? Aggregation.Percentile(_durations, 50) : null,
                P95 = _durations.Count > 0 ? Aggregation.Percentile(_durations, 95) : null,
                Max = _durations.Count > 0 ? _max : null,
                Status5xx = _s5xx,
                Top = _top.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(topN).Select(kv => kv.Key).ToList(),
                Trend = _trend,
                TrendP95 = trendP95,
                SlowestSeq = _slowestSeq,
                LastErrorSeq = _lastErrorSeq,
            };
        }
    }

    /// <summary>Subscribes to new events; dispose the reader's channel by cancelling.</summary>
    public ChannelReader<StoredEvent> Subscribe(CancellationToken cancellation)
    {
        var channel = Channel.CreateBounded<StoredEvent>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_subscribers) _subscribers.Add(channel.Writer);
        cancellation.Register(() =>
        {
            lock (_subscribers) _subscribers.Remove(channel.Writer);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    private StoredEvent At(int logicalIndex)
    {
        var start = (_head - _count + _ring.Length) % _ring.Length;
        return _ring[(start + logicalIndex) % _ring.Length]!;
    }

    private StoredEvent PAt(int logicalIndex)
    {
        var start = (_pHead - _pCount + _protected.Length) % _protected.Length;
        return _protected[(start + logicalIndex) % _protected.Length]!;
    }

    // Protected events were all evicted before anything now in the ring, and both buffers are FIFO,
    // so protected-then-ring is ordered by seq (oldest first).
    private StoredEvent[] Snapshot()
    {
        lock (_sync)
        {
            var all = new StoredEvent[_pCount + _count];
            for (var i = 0; i < _pCount; i++) all[i] = PAt(i);
            for (var i = 0; i < _count; i++) all[_pCount + i] = At(i);
            return all;
        }
    }

    private static void Bump(Dictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out var n) ? n + 1 : 1;

    private static Dictionary<string, int> Top(Dictionary<string, int> d, int n)
        => d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static List<HistogramBucket> BuildHistogram(List<(DateTimeOffset Time, LogLevel Level)> times, DateTimeOffset start, DateTimeOffset end, int buckets)
    {
        var result = new List<HistogramBucket>(buckets);
        if (buckets <= 0) return result;
        var span = end - start;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromSeconds(buckets);
        var width = TimeSpan.FromTicks(Math.Max(1, span.Ticks / buckets));
        var counts = new int[buckets];
        var warnings = new int[buckets];
        var errors = new int[buckets];
        foreach (var (t, level) in times)
        {
            var idx = (int)((t - start).Ticks / width.Ticks);
            if (idx < 0) idx = 0;
            if (idx >= buckets) idx = buckets - 1;
            counts[idx]++;
            if (level == LogLevel.Warning) warnings[idx]++;
            else if (level >= LogLevel.Error) errors[idx]++;
        }

        for (var i = 0; i < buckets; i++) result.Add(new HistogramBucket(start + TimeSpan.FromTicks(width.Ticks * i), counts[i], warnings[i], errors[i]));
        return result;
    }
}

/// <summary>One histogram bar with warning/error breakdown.</summary>
public sealed record HistogramBucket(DateTimeOffset T, int Count, int Warnings = 0, int Errors = 0);

/// <summary>Result of <see cref="LogStore.Query"/>.</summary>
public sealed class QueryResult
{
    /// <summary>Matching events, newest first, at most <c>limit</c>.</summary>
    public required IReadOnlyList<StoredEvent> Events { get; init; }
    /// <summary>Total matches (not limited).</summary>
    public required int Total { get; init; }
    /// <summary>Counts per level.</summary>
    public required IReadOnlyDictionary<string, int> Levels { get; init; }
    /// <summary>Top categories.</summary>
    public required IReadOnlyDictionary<string, int> Categories { get; init; }
    /// <summary>Top values per property key.</summary>
    public required IReadOnlyDictionary<string, Dictionary<string, int>> Properties { get; init; }
    /// <summary>Histogram over the effective range.</summary>
    public required IReadOnlyList<HistogramBucket> Histogram { get; init; }
    /// <summary>Effective range start.</summary>
    public required DateTimeOffset RangeStart { get; init; }
    /// <summary>Effective range end.</summary>
    public required DateTimeOffset RangeEnd { get; init; }
}
